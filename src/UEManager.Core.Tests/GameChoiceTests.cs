using UEManager.Core.Games;
using UEManager.Core.Storage;

namespace UEManager.Core.Tests;

public class GameChoiceTests
{
    private static readonly IReadOnlyList<GameProfile> Games =
    [
        new() { Id = "mortal-shell-2", DisplayName = "Mortal Shell II" },
        new() { Id = "phantom-blade-zero", DisplayName = "Phantom Blade Zero" },
    ];

    [Fact]
    public void A_hidden_game_starts_only_when_a_test_run_names_it()
    {
        IReadOnlyList<GameProfile> games =
        [
            new() { Id = "mortal-shell-2", DisplayName = "Mortal Shell II", Hidden = true },
            new() { Id = "phantom-blade-zero", DisplayName = "Phantom Blade Zero" },
            new() { Id = "stellar-blade-blood-rain", DisplayName = "Stellar Blade: Blood Rain", ComingSoon = true },
        ];

        // A choice saved before the game was hidden: the app asks again.
        Assert.Null(GameChoice.Resolve(GameChoice.Playable(games, null, true), null, "mortal-shell-2"));
        Assert.Equal("mortal-shell-2",
            GameChoice.Resolve(GameChoice.Playable(games, "mortal-shell-2", true), "mortal-shell-2", null)?.Id);
        // A game that is not out yet only where the build allows it.
        Assert.Equal(["phantom-blade-zero", "stellar-blade-blood-rain"], GameChoice.Playable(games, null, true).Select(g => g.Id));
        Assert.Equal(["phantom-blade-zero"], GameChoice.Playable(games, null, false).Select(g => g.Id));
    }

    [Fact]
    public void Without_a_choice_the_app_asks()
    {
        Assert.Null(GameChoice.Resolve(Games, null, null));
        Assert.Null(GameChoice.Resolve(Games, "", " "));
    }

    [Fact]
    public void The_chosen_game_starts_and_a_game_named_at_the_start_goes_first()
    {
        Assert.Equal("phantom-blade-zero", GameChoice.Resolve(Games, null, "phantom-blade-zero")?.Id);
        Assert.Equal("mortal-shell-2", GameChoice.Resolve(Games, "Mortal-Shell-2 ", "phantom-blade-zero")?.Id);
    }

    [Fact]
    public void A_player_who_chooses_at_every_start_is_asked_except_right_after_a_choice()
    {
        Assert.Null(GameChoice.Resolve(Games, null, "phantom-blade-zero", askAtStart: true));
        // The start that follows the choice names the game, so the picker does not come back at once.
        Assert.Equal("mortal-shell-2", GameChoice.Resolve(Games, "mortal-shell-2", "mortal-shell-2", askAtStart: true)?.Id);
        Assert.Equal("phantom-blade-zero", GameChoice.Resolve(Games, null, "phantom-blade-zero", askAtStart: false)?.Id);
    }

    [Fact]
    public void A_game_the_app_does_not_know_counts_as_no_choice()
    {
        Assert.Null(GameChoice.Resolve(Games, null, "stellar-blade"));
        Assert.Equal("phantom-blade-zero", GameChoice.Resolve(Games, "stellar-blade", "phantom-blade-zero")?.Id);
    }

    [Fact]
    public void The_chosen_game_survives_a_restart_and_old_settings_have_none()
    {
        using var folder = new TestFolder();
        var paths = new AppPaths(folder.Combine("Data"));
        Directory.CreateDirectory(paths.Root);
        File.WriteAllText(paths.SettingsFile, "{\"language\":\"zh-CN\"}");

        var settings = AppSettings.Load(paths);
        Assert.Null(settings.Game);

        settings.Game = "mortal-shell-2";
        settings.Save(paths);

        Assert.Equal("mortal-shell-2", AppSettings.Load(paths).Game);
        Assert.Equal("zh-CN", AppSettings.Load(paths).Language);
        Assert.False(AppSettings.Load(paths).AskForGameAtStart);
        // Older settings have not shown the developer's message yet, so it opens once.
        Assert.False(AppSettings.Load(paths).DeveloperMessageShown);

        settings.AskForGameAtStart = true;
        settings.Save(paths);
        Assert.True(AppSettings.Load(paths).AskForGameAtStart);
    }
}
