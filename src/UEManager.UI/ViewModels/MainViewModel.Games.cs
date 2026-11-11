using System.IO;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UEManager.Core;
using UEManager.Core.Games;
using UEManager.Core.Storage;
using UEManager.UI.Services;

namespace UEManager.UI.ViewModels;

/// <summary>"Switch game" in the title bar: the game picker over the blurred app, and the restart with the chosen game.</summary>
public sealed partial class MainViewModel
{
    /// <summary>The open game picker; null while it is closed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGamePickerOpen))]
    private GamePickerViewModel? gamePicker;

    /// <summary>The picker is open: the view behind it is blurred.</summary>
    public bool IsGamePickerOpen => GamePicker is not null;

    /// <summary>A game's card picture by its id. Tests, which have no app to read pictures from, put their own in its place.</summary>
    internal Func<string, ImageSource?> GameCard { get; set; } = ArtSet.Card;

    /// <summary>
    /// Where a game of the picker is on this PC; null looks in the saved folders, Steam and Epic. Tests, which must not read
    /// this PC's game lists, put their own in its place.
    /// </summary>
    internal Func<GameProfile, GameOnPc?>? LookUpGame { get; set; }

    /// <summary>Starts the app again with the game chosen, by its id. Tests put their own in its place.</summary>
    internal Func<string, bool> RestartForGame { get; set; } = game => App.Restart(game: game);

    /// <summary>The game's support is new and not checked on a real install yet: Home says Experimental.</summary>
    public bool IsExperimental => Profile.Experimental;

    /// <summary>What Experimental means, on the tag's tooltip.</summary>
    public string ExperimentalNote =>
        L.F($"Support for {GameDisplayName} is new. Its folders come from the mod pages and are not checked on a real install yet. If something does not work, tell us on Discord.");

    /// <summary>The player chooses the game at every start; see <see cref="AppSettings.AskForGameAtStart"/>.</summary>
    public bool AskForGameAtStart
    {
        get => settings.AskForGameAtStart;
        set
        {
            settings.AskForGameAtStart = value;
            SaveSettings();
            OnPropertyChanged();
        }
    }

    /// <summary>Not while a dialog or the busy screen is open: the title bar stays in reach above them.</summary>
    private bool CanSwitchGame => !HasOverlay;

    /// <summary>Opens the picker.</summary>
    [RelayCommand(CanExecute = nameof(CanSwitchGame))]
    private void SwitchGame()
    {
        if (HasOverlay)
            return;
        // Taken here: the cards look their games up away from the window's thread.
        var saved = new Dictionary<string, string>(settings.GamePaths, StringComparer.OrdinalIgnoreCase);
        GamePicker = new GamePickerViewModel(GameProfile.Offered(), Profile.Id, GameCard, ChooseGame,
            () => GamePicker = null, lookUp: LookUpGame ?? (game => GameOnPc.Look(game, saved.GetValueOrDefault(game.Id), paths)));
    }

    /// <summary>A game was chosen and the app is starting again: a second click does nothing.</summary>
    private bool switchingGame;

    /// <summary>
    /// The game of this run only closes the picker. Another game: the app saves the choice and starts again at once,
    /// so every part of it begins clean with that game's folders, mods and look.
    /// </summary>
    private void ChooseGame(GameProfile game)
    {
        if (switchingGame)
            return;
        if (game.Id.Equals(Profile.Id, StringComparison.OrdinalIgnoreCase))
        {
            GamePicker = null;
            return;
        }
        var name = L.T(game.DisplayName);
        var previous = settings.Game;
        settings.Game = game.Id;
        try
        {
            settings.Save(paths);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            settings.Game = previous;
            Log.Error("The chosen game could not be saved.", ex);
            ShowToast(L.F($"Settings could not be saved: {ex.Message}"), isError: true);
            return;
        }
        Log.Info($"Switching to '{game.Id}'; starting again.");
        switchingGame = true;
        if (RestartForGame(game.Id))
            return;
        switchingGame = false;
        GamePicker = null;
        ShowToast(L.F($"The app could not start again. Close it and start it again: it then opens with {name}."), isError: true);
    }
}
