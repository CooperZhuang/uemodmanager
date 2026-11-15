using System.Text;
using UEManager.Core.Games;
using UEManager.Core.Storage;

namespace UEManager.Core.Tests;

/// <summary>Data\serve.json: a download made for one game names the game the picker puts in the middle.</summary>
public class ServedGameTests
{
    private static readonly IReadOnlyList<GameProfile> Games =
    [
        new() { Id = "mortal-shell-2", DisplayName = "Mortal Shell II", Hidden = true },
        new() { Id = "phantom-blade-zero", DisplayName = "Phantom Blade Zero", Featured = true },
        new() { Id = "stellar-blade-blood-rain", DisplayName = "Stellar Blade: Blood Rain", ComingSoon = true },
        new() { Id = "wuchang-fallen-feathers", DisplayName = "WUCHANG: Fallen Feathers" },
    ];

    [Theory]
    [InlineData("""{"gameToServe": "phantom-blade-zero"}""", "phantom-blade-zero")]
    // Written by hand: other spelling of the key, spaces, a comment, a trailing comma.
    [InlineData("""{ "GameToServe": "  wuchang-fallen-feathers ", }""", "wuchang-fallen-feathers")]
    [InlineData("""/* for the WUCHANG download */ {"gameToServe": "wuchang-fallen-feathers"}""", "wuchang-fallen-feathers")]
    [InlineData("""{"gameToServe": ""}""", null)]
    [InlineData("""{"gameToServe": null}""", null)]
    [InlineData("""{"gameToServe": 7}""", null)]
    [InlineData("""{"game": "phantom-blade-zero"}""", null)]
    [InlineData("""["phantom-blade-zero"]""", null)]
    [InlineData("not json", null)]
    [InlineData("", null)]
    public void The_file_names_a_game_or_nothing(string content, string? expected)
    {
        using var folder = new TestFolder();
        var paths = new AppPaths(folder.Combine("Data"));
        Directory.CreateDirectory(paths.Root);
        File.WriteAllText(paths.ServeFile, content);

        Assert.Equal(expected, ServedGame.Read(paths));
    }

    /// <summary>Notepad may save the file with a byte order mark.</summary>
    [Fact]
    public void A_byte_order_mark_does_not_matter()
    {
        using var folder = new TestFolder();
        var paths = new AppPaths(folder.Combine("Data"));
        Directory.CreateDirectory(paths.Root);
        File.WriteAllText(paths.ServeFile, """{"gameToServe": "phantom-blade-zero"}""", new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        Assert.Equal(0xEF, File.ReadAllBytes(paths.ServeFile)[0]);
        Assert.Equal("phantom-blade-zero", ServedGame.Read(paths));
    }

    [Fact]
    public void Without_the_file_nothing_is_served_and_removing_it_is_no_error()
    {
        using var folder = new TestFolder();
        var paths = new AppPaths(folder.Combine("Data"));

        Assert.False(ServedGame.Exists(paths));
        Assert.Null(ServedGame.Read(paths));
        Assert.True(ServedGame.Remove(paths));
    }

    [Fact]
    public void Removing_the_file_ends_the_serving_and_keeps_the_settings()
    {
        using var folder = new TestFolder();
        var paths = new AppPaths(folder.Combine("Data"));
        Directory.CreateDirectory(paths.Root);
        File.WriteAllText(paths.ServeFile, """{"gameToServe": "phantom-blade-zero"}""");
        new AppSettings { Language = "vi" }.Save(paths);

        Assert.True(ServedGame.Remove(paths));

        Assert.False(ServedGame.Exists(paths));
        Assert.Null(ServedGame.Read(paths));
        Assert.Equal("vi", AppSettings.Load(paths).Language);
    }

    [Fact]
    public void Only_a_game_the_picker_offers_is_served_and_only_while_none_is_chosen()
    {
        Assert.Equal("phantom-blade-zero", GameChoice.Served(Games, "phantom-blade-zero", null, false)?.Id);
        Assert.Equal("phantom-blade-zero", GameChoice.Served(Games, " Phantom-Blade-Zero ", " ", false)?.Id);
        // Not a highlight, but in All games: served too.
        Assert.Equal("wuchang-fallen-feathers", GameChoice.Served(Games, "wuchang-fallen-feathers", null, false)?.Id);

        // The player chose a game before: the picker is as usual.
        Assert.Null(GameChoice.Served(Games, "phantom-blade-zero", "wuchang-fallen-feathers", false));
        // Unknown, hidden, or not out yet in this build.
        Assert.Null(GameChoice.Served(Games, "phantom-blade", null, false));
        Assert.Null(GameChoice.Served(Games, "mortal-shell-2", null, false));
        Assert.Null(GameChoice.Served(Games, "stellar-blade-blood-rain", null, false));
        Assert.Equal("stellar-blade-blood-rain", GameChoice.Served(Games, "stellar-blade-blood-rain", null, true)?.Id);
        Assert.Null(GameChoice.Served(Games, null, null, false));
    }
}
