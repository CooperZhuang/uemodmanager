using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UEManager.Core;
using UEManager.Core.Games;
using UEManager.UI.Services;

namespace UEManager.UI.ViewModels;

/// <summary>
/// The launch options: the start arguments "Launch game" passes to the game, set in a window from the right part of the
/// button. A game's profile can recommend some, as Black Myth: Wukong's -fileopenlog; the player's own replace them.
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>The launch options window is open.</summary>
    [ObservableProperty]
    private bool isLaunchOptionsOpen;

    /// <summary>The arguments in the window's field, saved only with Save.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UseRecommendedLaunchArgumentsCommand))]
    private string launchOptionsText = "";

    /// <summary>The arguments "Launch game" starts the game with now; null for none.</summary>
    internal string? LaunchArguments => LaunchOptions.For(settings, Profile);

    /// <summary>The tooltip of the button's right part.</summary>
    public string LaunchOptionsHint => LaunchArguments is { } arguments ? L.F($"Launch options: {arguments}") : L.T("Launch options: none");

    public string LaunchOptionsIntro => L.F($"\"Launch game\" starts {GameDisplayName} with these arguments. Separate them with spaces; leave the field empty for none.");

    /// <summary>The arguments the game's profile recommends; null for none.</summary>
    public string? RecommendedLaunchArguments => LaunchOptions.Recommended(Profile);

    public bool HasRecommendedLaunchArguments => RecommendedLaunchArguments is not null;

    public string RecommendedLaunchText => RecommendedLaunchArguments is { } recommended
        ? L.F($"Recommended for {GameDisplayName}: {recommended}. Some mods do not load without it.")
        : "";

    /// <summary>
    /// Whether the store's own launch options apply. A game from Steam or Epic starts from its own exe with arguments, and
    /// through the store without. A game in a folder chosen by hand always starts from its own exe. Null without a game.
    /// </summary>
    public string? LaunchOptionsStoreNote => Game switch
    {
        { LaunchUri: not null } install => L.F($"With arguments, the game starts from its own exe, so the launch options set in {(install.Store == GameStore.Epic ? "Epic Games" : "Steam")} do not apply. Without arguments, it starts through the store, and they apply."),
        not null => L.T("This copy starts from its own exe, so launch options set in Steam or Epic do not apply."),
        null => null,
    };

    [RelayCommand]
    private void OpenLaunchOptions()
    {
        LaunchOptionsText = LaunchArguments ?? "";
        OnPropertyChanged(nameof(LaunchOptionsStoreNote));
        IsLaunchOptionsOpen = true;
    }

    private bool CanUseRecommendedLaunchArguments() =>
        RecommendedLaunchArguments is { } recommended && LaunchOptions.Clean(LaunchOptionsText) != recommended;

    [RelayCommand(CanExecute = nameof(CanUseRecommendedLaunchArguments))]
    private void UseRecommendedLaunchArguments() => LaunchOptionsText = RecommendedLaunchArguments ?? "";

    [RelayCommand]
    private void SaveLaunchOptions()
    {
        LaunchOptions.Set(settings, Profile, LaunchOptionsText);
        SaveSettings();
        Log.Info(LaunchArguments is { } arguments ? $"Launch options: {arguments}" : "Launch options: none");
        OnPropertyChanged(nameof(LaunchOptionsHint));
        IsLaunchOptionsOpen = false;
    }

    [RelayCommand]
    private void CloseLaunchOptions() => IsLaunchOptionsOpen = false;
}
