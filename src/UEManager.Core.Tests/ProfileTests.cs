using UEManager.Core.Games;

namespace UEManager.Core.Tests;

public class ProfileTests
{
    [Fact]
    public void Built_in_profiles_are_valid()
    {
        var profiles = GameProfile.BuiltIn();

        var pbz = Assert.Single(profiles, p => p.Id == "phantom-blade-zero");
        Assert.Equal("Phantom Blade Zero", pbz.DisplayName);
        Assert.Equal("4115450", pbz.SteamAppId);
        Assert.Equal("https://github.com/KintaroModding/ue4ss/releases", pbz.Ue4ssDownloadUrl);
        Assert.All(profiles, p =>
        {
            Assert.False(string.IsNullOrWhiteSpace(p.PaksPath));
            Assert.False(string.IsNullOrWhiteSpace(p.ModsPath));
        });
        Assert.Equal(profiles.Count, profiles.Select(p => p.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void The_app_contains_every_game_ordered_by_name()
    {
        Assert.Equal(["black-myth-wukong", "mortal-shell-2", "phantom-blade-zero", "stellar-blade-blood-rain"], GameProfile.BuiltIn().Select(p => p.Id));
        Assert.Equal(["stellar-blade-blood-rain"], GameProfile.BuiltIn().Where(p => p.ComingSoon).Select(p => p.Id));
        Assert.Equal(["mortal-shell-2"], GameProfile.BuiltIn().Where(p => p.Experimental).Select(p => p.Id));
        // Mortal Shell II stays in the app but is not offered for 2.0.0.
        Assert.Equal(["mortal-shell-2"], GameProfile.BuiltIn().Where(p => p.Hidden).Select(p => p.Id));
        Assert.Equal(["black-myth-wukong", "phantom-blade-zero", "stellar-blade-blood-rain"], GameProfile.Offered().Select(p => p.Id));
    }

    [Fact]
    public void Every_profile_file_is_valid()
    {
        var files = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Profiles"), "*.json");

        Assert.Contains(files, f => Path.GetFileName(f) == "mortal-shell-2.json");
        Assert.All(files, file =>
        {
            var profile = GameProfile.Parse(File.ReadAllText(file), Path.GetFileName(file));
            Assert.Equal(Path.GetFileNameWithoutExtension(file), profile.Id);
            Assert.True(profile.SteamAppId is null || profile.SteamAppId.All(char.IsAsciiDigit));
        });
    }

    [Fact]
    public void Comments_and_trailing_commas_are_allowed()
    {
        var profile = GameProfile.Parse("""
            // a comment
            { "id": "x", "displayName": "X", "modsPath": "Mods", }
            """, "test");

        Assert.Equal("Mods", profile.ModsPath);
        Assert.Equal("{Project}/Content/Paks", profile.PaksPath);
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("""{ "id": "x" }""")]
    public void Broken_profiles_are_refused(string json)
    {
        Assert.Throws<InvalidDataException>(() => GameProfile.Parse(json, "test"));
    }
}
