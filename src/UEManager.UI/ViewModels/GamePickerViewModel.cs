using System.ComponentModel;
using System.IO;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UEManager.Core;
using UEManager.Core.Games;
using UEManager.Core.Mods;
using UEManager.Core.Storage;
using UEManager.UI.Services;

namespace UEManager.UI.ViewModels;

/// <summary>
/// What a game's card says about this PC: the store the game was found in (null when it was not found), and how many
/// mods the app manages for it.
/// </summary>
public sealed record GameOnPc(GameStore? Store, int Mods)
{
    /// <summary>
    /// Looks the game up as the app does at start: in the folder the player chose before, else in Steam and Epic. The mods
    /// come from the game's mod list. It only reads files; nothing is changed.
    /// </summary>
    public static GameOnPc Look(GameProfile game, string? savedFolder, AppPaths paths) =>
        new(GameLocator.Find(game, savedFolder)?.Store, ModManager.CountMods(paths, game.Id));
}

/// <summary>A game in the picker: its picture above its name.</summary>
public sealed class GameCardViewModel(GameProfile game, ImageSource? picture, bool isCurrent) : LocalizedObservableObject
{
    public GameProfile Game => game;

    public string Name => L.T(game.DisplayName);

    public ImageSource? Picture => picture;

    public bool HasPicture => picture is not null;

    /// <summary>The game the app runs for now; no game is, while the app asks at the start.</summary>
    public bool IsCurrent => isCurrent;

    /// <summary>The game is not out yet: the card says so.</summary>
    public bool IsComingSoon => game.ComingSoon;

    /// <summary>The game's support is new and not checked on a real install yet: the card says so.</summary>
    public bool IsExperimental => game.Experimental;

    /// <summary>A card of a game that is not out yet can be chosen only while the app allows it, for tests of its look.</summary>
    public bool CanChoose => !game.ComingSoon || AppInfo.CanChooseComingGames;

    /// <summary>
    /// A game that is not out yet invites a click: the app then shows itself in the game's look. Not on the card of the
    /// game the app shows now.
    /// </summary>
    public bool InvitesPeek => game.ComingSoon && CanChoose && !isCurrent;

    private GameOnPc? onPc;

    /// <summary>The game on this PC; null until it is looked up, and for a game that is not out yet.</summary>
    public GameOnPc? OnPc
    {
        get => onPc;
        set
        {
            onPc = value;
            OnPropertyChanged(string.Empty);
        }
    }

    public bool HasStatus => onPc is not null;

    public bool IsFound => onPc?.Store is not null;

    /// <summary>The mods alone, for a row of the tray menu: "3 mods", or nothing.</summary>
    public string ModsText => onPc is { Mods: > 0 } ? L.Plural(onPc.Mods, "{0} mod", "{0} mods") : "";

    /// <summary>The label on the card's picture, such as "Steam  ·  3 mods" or "Game not found".</summary>
    public string StatusText => onPc is null
        ? ""
        : string.Join("  ·  ", new[]
        {
            onPc.Store switch
            {
                GameStore.Steam => "Steam",
                GameStore.Epic => "Epic Games",
                GameStore.Manual => L.T("Chosen by hand"),
                _ => L.T("Game not found"),
            },
            onPc.Mods > 0 ? L.Plural(onPc.Mods, "{0} mod", "{0} mods") : null,
        }.Where(part => part is not null));
}

/// <summary>
/// The game picker: a card per game the app knows. It shows at a start without a chosen game, and over the
/// blurred app after "Switch game". A choice starts the app again with that game.
/// </summary>
public sealed partial class GamePickerViewModel : LocalizedObservableObject
{
    private readonly Action<GameProfile> choose;
    private readonly Action? close;
    private readonly Action<string>? chooseLanguage;

    /// <param name="games">By name: the cards show in this order, the games that are not out yet last.</param>
    /// <param name="currentId">The id of the game the app runs for; null at a start without one.</param>
    /// <param name="picture">A game's card picture by its id; null when it has none.</param>
    /// <param name="choose">Takes the game of the card the player chose.</param>
    /// <param name="close">Closes the picker without a choice; null when the app cannot go on without one.</param>
    /// <param name="chooseLanguage">
    /// Shows and saves a language by its code; null when the picker offers none, as after "Switch game", where Settings has them.
    /// </param>
    /// <param name="lookUp">Where a game is on this PC, for its card; null leaves the cards without it.</param>
    public GamePickerViewModel(IEnumerable<GameProfile> games, string? currentId, Func<string, ImageSource?> picture,
        Action<GameProfile> choose, Action? close, Action<string>? chooseLanguage = null, Func<GameProfile, GameOnPc?>? lookUp = null)
    {
        this.choose = choose;
        this.close = close;
        this.chooseLanguage = chooseLanguage;
        Cards = games
            .OrderBy(game => game.ComingSoon)
            .Select(game => new GameCardViewModel(game, picture(game.Id),
                game.Id.Equals(currentId, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        LookedUp = lookUp is null ? Task.CompletedTask : LookUpAsync(lookUp);
    }

    public IReadOnlyList<GameCardViewModel> Cards { get; }

    /// <summary>The look-up of the cards' games on this PC; done at once without one. Tests wait for it.</summary>
    internal Task LookedUp { get; }

    /// <summary>
    /// Fills in where each game is on this PC, away from the window's thread: it reads the Steam and Epic lists and the
    /// mod lists from disk. A game that is not out yet is not looked up.
    /// </summary>
    private async Task LookUpAsync(Func<GameProfile, GameOnPc?> lookUp)
    {
        var cards = Cards.Where(card => !card.IsComingSoon).ToList();
        var found = await Task.Run(() => cards.Select(card => LookUp(lookUp, card.Game)).ToList());
        for (var i = 0; i < cards.Count; i++)
            cards[i].OnPc = found[i];
    }

    private static GameOnPc? LookUp(Func<GameProfile, GameOnPc?> lookUp, GameProfile game)
    {
        try
        {
            return lookUp(game);
        }
        // A card without its label is no loss: the picker opens whatever a look-up runs into.
        catch (Exception ex)
        {
            Log.Warn($"{game.DisplayName} could not be looked up on this PC: {ex.Message}");
            return null;
        }
    }

    public bool CanClose => close is not null;

    /// <summary>
    /// The picker at a start without a chosen game offers the languages, so a player reads the cards and the developer's
    /// message in their own language. After "Switch game", Settings has them.
    /// </summary>
    public bool CanChooseLanguage => chooseLanguage is not null;

    public IReadOnlyList<LanguageOption> Languages => L.Current.Languages;

    /// <summary>The language shown; a choice shows at once and is saved.</summary>
    public string SelectedLanguage
    {
        get => L.Current.Language;
        set
        {
            if (value != L.Current.Language && Languages.Any(language => language.Code == value))
                chooseLanguage?.Invoke(value);
        }
    }

    /// <summary>The question that offers the language of Windows; the cards show once it is answered. Null when none is open.</summary>
    [ObservableProperty]
    private ConfirmViewModel? question;

    /// <summary>
    /// Asks a player who chose no language yet whether to switch to the language of Windows, before the cards and the
    /// developer's message show. Either answer is saved, so the app does not ask again once it runs with a game.
    /// </summary>
    internal async Task OfferLanguageAsync(LanguageOption offered)
    {
        var asked = ConfirmViewModel.LanguageOffer(offered);
        Question = asked;
        var accepted = await asked.Result;
        Question = null;
        chooseLanguage?.Invoke(accepted ? offered.Code : L.English);
    }

    /// <summary>The developer's message above the cards is open. It is closed until the player opens it.</summary>
    [ObservableProperty]
    private bool isMessageOpen;

    /// <summary>
    /// The panel for a game the app does not know yet is open: the scan of its folder, and Discord. Only one of the two
    /// panels is open at a time.
    /// </summary>
    [ObservableProperty]
    private bool isMissingGameOpen;

    partial void OnIsMessageOpenChanged(bool value)
    {
        if (value)
            IsMissingGameOpen = false;
    }

    partial void OnIsMissingGameOpenChanged(bool value)
    {
        if (value)
            IsMessageOpen = false;
    }

    /// <summary>The scan of a game folder, in the panel for a missing game; its report shows in place of the picker.</summary>
    public FolderScanViewModel Scan { get; } = new();

    /// <summary>The message invites the player to the community on Discord.</summary>
    [RelayCommand]
    private void OpenDiscord() => Open(AppInfo.DiscordUrl);

    /// <summary>The message invites the player to contribute to the project on GitHub, the public repository.</summary>
    [RelayCommand]
    private void OpenGitHub() => Open(AppInfo.ProjectUrl);

    private static void Open(string url)
    {
        try
        {
            Shell.Open(url);
        }
        catch (Exception ex) when (ex is Win32Exception or IOException)
        {
            Log.Warn($"'{url}' could not be opened: {ex.Message}");
        }
    }

    [RelayCommand(CanExecute = nameof(CanChoose))]
    private void Choose(GameCardViewModel? card)
    {
        if (card is not null)
            choose(card.Game);
    }

    private static bool CanChoose(GameCardViewModel? card) => card?.CanChoose == true;

    [RelayCommand]
    private void Close() => close?.Invoke();
}
