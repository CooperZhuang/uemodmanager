using System.ComponentModel;
using UEManager.Core.Games;

namespace UEManager.UI.ViewModels;

/// <summary>The tray icon: its three settings, and its menu.</summary>
public sealed partial class MainViewModel
{
    /// <summary>The icon next to the clock. On until the player turns it off; without it, the app works as before.</summary>
    public bool ShowTrayIcon
    {
        get => settings.ShowTrayIcon;
        set
        {
            settings.ShowTrayIcon = value;
            SaveSettings();
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanCloseToTray));
        }
    }

    /// <summary>Minimize hides the window in the tray, without a taskbar button. Off until the player turns it on.</summary>
    public bool MinimizeToTray
    {
        get => settings.MinimizeToTray;
        set
        {
            settings.MinimizeToTray = value;
            SaveSettings();
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanCloseToTray));
        }
    }

    /// <summary>The X hides the window in the tray instead of closing the app; only with both settings above on.</summary>
    public bool CloseToTray
    {
        get => settings.CloseToTray;
        set
        {
            settings.CloseToTray = value;
            SaveSettings();
            OnPropertyChanged();
        }
    }

    /// <summary>The X can hide the app only when the icon shows and minimize hides it: its box waits for both.</summary>
    public bool CanCloseToTray => settings.ShowTrayIcon && settings.MinimizeToTray;

    /// <summary>Minimize hides the window in the tray.</summary>
    internal bool MinimizesToTray => settings.ShowTrayIcon && settings.MinimizeToTray;

    /// <summary>The X hides the window in the tray.</summary>
    internal bool ClosesToTray => MinimizesToTray && settings.CloseToTray;

    /// <summary>The tray icon's menu, as it opens now.</summary>
    /// <param name="open">Shows the window.</param>
    /// <param name="exit">Ends the app.</param>
    internal TrayMenuViewModel TrayMenu(Action open, Action exit)
    {
        // Taken here: the rows look their games up away from the window's thread.
        var saved = new Dictionary<string, string>(settings.GamePaths, StringComparer.OrdinalIgnoreCase);
        return new TrayMenuViewModel(GameProfile.Offered(), Profile.Id, GameCard,
            LookUpGame ?? (game => GameOnPc.Look(game, saved.GetValueOrDefault(game.Id), paths)),
            CanSwitchGame, ChooseGame, () => _ = LaunchFromTrayAsync(open), open, exit);
    }

    /// <summary>
    /// "Launch game" in the tray menu: the game starts without the window. When the start needs the player, as for a
    /// question or an error, the window comes back to show it.
    /// </summary>
    internal async Task LaunchFromTrayAsync(Action show)
    {
        void OnChanged(object? sender, PropertyChangedEventArgs e)
        {
            if ((e.PropertyName == nameof(Confirm) && Confirm is not null) || (e.PropertyName == nameof(Toast) && Toast is { IsError: true }))
                show();
        }

        PropertyChanged += OnChanged;
        try
        {
            await LaunchGameCommand.ExecuteAsync(null);
        }
        finally
        {
            PropertyChanged -= OnChanged;
        }
    }
}
