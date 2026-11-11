using System.Windows.Media;
using CommunityToolkit.Mvvm.Input;
using UEManager.Core.Games;
using UEManager.UI.Services;

namespace UEManager.UI.ViewModels;

/// <summary>
/// The tray icon's menu: the games, for a switch with one click, and the app's main actions. A new one opens at every
/// right-click, so it shows the app as it is then.
/// </summary>
public sealed partial class TrayMenuViewModel : LocalizedObservableObject
{
    private readonly Action<GameProfile> choose;
    private readonly Action launch;
    private readonly Action open;
    private readonly Action exit;

    /// <param name="games">The games, in the picker's order; a game that is not out yet shows dimmed.</param>
    /// <param name="currentId">The game the app runs for: its row is marked, and a click on it opens the window.</param>
    /// <param name="picture">A game's card picture by its id.</param>
    /// <param name="lookUp">Where a game is on this PC, for its mod count; null leaves the rows without it.</param>
    /// <param name="canSwitch">False while a dialog or a step is open: the games wait, as the Switch game button does.</param>
    /// <param name="choose">Takes another game than the one of this run; the app then starts again with it.</param>
    public TrayMenuViewModel(IEnumerable<GameProfile> games, string currentId, Func<string, ImageSource?> picture,
        Func<GameProfile, GameOnPc?>? lookUp, bool canSwitch, Action<GameProfile> choose, Action launch, Action open, Action exit)
    {
        this.choose = choose;
        this.launch = launch;
        this.open = open;
        this.exit = exit;
        CanSwitch = canSwitch;
        // The picker's cards, with their pictures and the look-up of their mods.
        var picker = new GamePickerViewModel(games, currentId, picture, _ => { }, close: null, lookUp: lookUp);
        Cards = picker.Cards;
        LookedUp = picker.LookedUp;
    }

    public IReadOnlyList<GameCardViewModel> Cards { get; }

    /// <summary>The look-up of the games' mods on this PC. Tests wait for it.</summary>
    internal Task LookedUp { get; }

    public bool CanSwitch { get; }

    public string Title => $"{AppInfo.Name}  ·  {L.T("Switch game")}";

    public string OpenText => L.F($"Open {AppInfo.Name}");

    /// <summary>A click ran: the menu closes.</summary>
    public event Action? Finished;

    [RelayCommand(CanExecute = nameof(CanChoose))]
    private void Choose(GameCardViewModel? card)
    {
        if (card is null)
            return;
        Finished?.Invoke();
        if (card.IsCurrent)
            open();
        else
            choose(card.Game);
    }

    private bool CanChoose(GameCardViewModel? card) => CanSwitch && card?.CanChoose == true;

    [RelayCommand]
    private void LaunchGame()
    {
        Finished?.Invoke();
        launch();
    }

    [RelayCommand]
    private void Open()
    {
        Finished?.Invoke();
        open();
    }

    [RelayCommand]
    private void Exit()
    {
        Finished?.Invoke();
        exit();
    }
}
