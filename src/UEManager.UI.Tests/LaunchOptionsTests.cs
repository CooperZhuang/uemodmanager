using System.IO;
using UEManager.Core.Games;
using UEManager.Core.Storage;
using UEManager.UI.Services;
using UEManager.UI.ViewModels;
using Xunit;

namespace UEManager.UI.Tests;

/// <summary>The launch options window behind the right part of "Launch game": the start arguments the game gets.</summary>
public class LaunchOptionsTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "uemm-launch-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(folder))
            Directory.Delete(folder, recursive: true);
    }

    private static readonly GameProfile Wukong = new() { Id = "black-myth-wukong", DisplayName = "Black Myth: Wukong", LaunchArguments = "-fileopenlog" };

    private MainViewModel App(GameProfile profile)
    {
        var paths = new AppPaths(Path.Combine(folder, "Data"));
        paths.EnsureWritable();
        return new MainViewModel(profile, paths, new ArtSet(null, null)) { GameCard = _ => null, LookUpGame = _ => null };
    }

    [Fact]
    public void The_window_starts_with_the_recommended_arguments_and_saves_the_players_own() => UiThread.Run(() =>
    {
        var model = App(Wukong);
        Assert.Equal("Launch options: -fileopenlog", model.LaunchOptionsHint);

        model.OpenLaunchOptionsCommand.Execute(null);
        Assert.True(model.IsLaunchOptionsOpen);
        Assert.True(model.HasOverlay);
        Assert.Equal("-fileopenlog", model.LaunchOptionsText);
        Assert.Equal("Recommended for Black Myth: Wukong: -fileopenlog. Some mods do not load without it.", model.RecommendedLaunchText);
        Assert.False(model.UseRecommendedLaunchArgumentsCommand.CanExecute(null));

        // Cleared: none at all, also after a restart.
        model.LaunchOptionsText = "";
        Assert.True(model.UseRecommendedLaunchArgumentsCommand.CanExecute(null));
        model.SaveLaunchOptionsCommand.Execute(null);
        Assert.False(model.IsLaunchOptionsOpen);
        Assert.Null(model.LaunchArguments);
        Assert.Equal("Launch options: none", model.LaunchOptionsHint);
        Assert.Null(App(Wukong).LaunchArguments);

        // Cancel keeps what was saved.
        model.OpenLaunchOptionsCommand.Execute(null);
        model.LaunchOptionsText = "-dx11";
        model.CloseLaunchOptionsCommand.Execute(null);
        Assert.Null(model.LaunchArguments);

        // Back to the recommendation with one click.
        model.OpenLaunchOptionsCommand.Execute(null);
        model.UseRecommendedLaunchArgumentsCommand.Execute(null);
        Assert.Equal("-fileopenlog", model.LaunchOptionsText);
        model.SaveLaunchOptionsCommand.Execute(null);
        Assert.Equal("-fileopenlog", App(Wukong).LaunchArguments);
    });

    [Fact]
    public void The_window_says_whether_the_launch_options_set_in_Steam_apply() => UiThread.Run(() =>
    {
        var model = App(Wukong);
        var root = Path.Combine(folder, "Game");
        GameInstall Install(GameStore store, string? launchUri) =>
            new(Wukong, root, "b1", root, root, root, store, LaunchUri: launchUri);

        Assert.Null(model.LaunchOptionsStoreNote);

        // A folder chosen by hand never starts through a store.
        model.Game = Install(GameStore.Manual, null);
        model.OpenLaunchOptionsCommand.Execute(null);
        Assert.Equal("This copy starts from its own exe, so launch options set in Steam or Epic do not apply.", model.LaunchOptionsStoreNote);

        model.Game = Install(GameStore.Steam, "steam://rungameid/2358720");
        model.OpenLaunchOptionsCommand.Execute(null);
        Assert.Equal("With arguments, the game starts from its own exe, so the launch options set in Steam do not apply. "
            + "Without arguments, it starts through the store, and they apply.", model.LaunchOptionsStoreNote);
        // Also with an empty field: the player should know it either way.
        model.LaunchOptionsText = "";
        Assert.NotNull(model.LaunchOptionsStoreNote);
    });

    [Fact]
    public void A_game_without_a_recommendation_has_no_arguments_until_the_player_adds_some() => UiThread.Run(() =>
    {
        var pbz = new GameProfile { Id = "phantom-blade-zero", DisplayName = "Phantom Blade Zero" };
        var model = App(pbz);
        Assert.False(model.HasRecommendedLaunchArguments);
        Assert.Equal("Launch options: none", model.LaunchOptionsHint);

        model.OpenLaunchOptionsCommand.Execute(null);
        Assert.Equal("", model.LaunchOptionsText);
        Assert.False(model.UseRecommendedLaunchArgumentsCommand.CanExecute(null));
        model.LaunchOptionsText = "  -dx12 ";
        model.SaveLaunchOptionsCommand.Execute(null);

        Assert.Equal("-dx12", App(pbz).LaunchArguments);
        Assert.Equal("Launch options: -dx12", model.LaunchOptionsHint);
    });
}
