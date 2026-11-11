using UEManager.Core.Games;
using UEManager.Core.Storage;

namespace UEManager.Core.Tests;

/// <summary>"Launch game" with the start arguments a game needs, as Black Myth: Wukong's -fileopenlog for pak mods.</summary>
public class LaunchArgumentTests
{
    private static GameProfile Profile(string? arguments) => new()
    {
        Id = "test-game",
        DisplayName = "Test Game",
        SteamAppId = "999999",
        ProjectFolder = "b1",
        LaunchArguments = arguments,
    };

    [Fact]
    public void A_game_that_needs_start_arguments_starts_from_its_exe_with_them()
    {
        using var folder = new TestFolder();
        var root = folder.Game("b1");
        var steam = GameLocator.Resolve(Profile("-fileopenlog"), root, GameStore.Steam, launchUri: "steam://rungameid/2358720")!;

        var (target, arguments, workingFolder) = steam.LaunchCommand();

        Assert.Equal(Path.Combine(root, "b1", "Binaries", "Win64", "b1-Win64-Shipping.exe"), target);
        Assert.Equal("-fileopenlog", arguments);
        Assert.Equal(Path.Combine(root, "b1", "Binaries", "Win64"), workingFolder);
    }

    [Fact]
    public void Without_arguments_the_store_starts_the_game()
    {
        using var folder = new TestFolder();
        var steam = GameLocator.Resolve(Profile(null), folder.Game("b1"), GameStore.Steam, launchUri: "steam://rungameid/2358720")!;

        Assert.Equal(("steam://rungameid/2358720", (string?)null, (string?)null), steam.LaunchCommand());
    }

    [Fact]
    public void The_players_arguments_start_the_exe_and_none_start_through_the_store()
    {
        using var folder = new TestFolder();
        var root = folder.Game("b1");
        var steam = GameLocator.Resolve(Profile(null), root, GameStore.Steam, launchUri: "steam://rungameid/2358720")!;

        Assert.Equal((Path.Combine(root, "b1", "Binaries", "Win64", "b1-Win64-Shipping.exe"), "-dx12 -windowed",
            Path.Combine(root, "b1", "Binaries", "Win64")), steam.LaunchCommand("  -dx12 -windowed "));
        Assert.Equal(("steam://rungameid/2358720", (string?)null, (string?)null), steam.LaunchCommand("   "));
    }

    [Fact]
    public void The_players_arguments_replace_the_recommended_ones_and_survive_a_restart()
    {
        using var folder = new TestFolder();
        var paths = new AppPaths(folder.Combine("Data"));
        Directory.CreateDirectory(paths.Root);
        var settings = new AppSettings();
        var wukong = Profile("-fileopenlog");

        Assert.Equal("-fileopenlog", LaunchOptions.For(settings, wukong));

        // None at all: kept as an empty text, so the recommendation does not come back.
        LaunchOptions.Set(settings, wukong, " ");
        settings.Save(paths);
        Assert.Null(LaunchOptions.For(AppSettings.Load(paths), wukong));

        LaunchOptions.Set(settings, wukong, "-fileopenlog -dx11");
        Assert.Equal("-fileopenlog -dx11", LaunchOptions.For(settings, wukong));

        // The recommended ones are not kept: a later recommendation reaches the player.
        LaunchOptions.Set(settings, wukong, " -fileopenlog ");
        Assert.Empty(settings.LaunchArguments);
        Assert.Equal("-dx12", LaunchOptions.For(settings, Profile("-dx12")));
        Assert.Null(LaunchOptions.For(settings, Profile(null)));
    }

    [Fact]
    public void Wukong_starts_with_fileopenlog()
    {
        var wukong = GameProfile.BuiltIn().Single(p => p.Id == "black-myth-wukong");

        Assert.Equal("-fileopenlog", wukong.LaunchArguments);
        Assert.False(wukong.Experimental);
    }
}
