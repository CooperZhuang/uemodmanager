using UEManager.UI.Services;
using Xunit;

namespace UEManager.UI.Tests;

/// <summary>CHANGELOG.md, which the Changelog link in the sidebar shows.</summary>
public class ChangelogTests
{
    [Fact]
    public void Releases_come_newest_first_with_their_date_and_one_line_per_change()
    {
        var releases = Changelog.Parse("""
            # Changelog

            What changed in each release.
            - not a change: before the first release

            ## 1.4.0 (2026-11-01)
            - Loader mods next to the exe
            - Saves never touched

            ## v1.3.2
            - Rename installed mods
            """);

        Assert.Equal(["1.4.0", "1.3.2"], releases.Select(r => r.Version));
        Assert.Equal(new DateOnly(2026, 11, 1), releases[0].Date);
        Assert.Null(releases[1].Date);
        Assert.Equal(["Loader mods next to the exe", "Saves never touched"], releases[0].Changes);
        Assert.Equal(["Rename installed mods"], releases[1].Changes);
    }

    [Fact]
    public void A_translation_replaces_the_lines_it_has_and_English_shows_for_the_rest()
    {
        var english = Changelog.Parse("""
            ## 1.4.0 (2026-11-01)
            - Loader mods next to the exe
            ## 1.3.2 (2026-11-01)
            - Rename installed mods
            """);
        var chinese = Changelog.Parse("""
            ## 1.3.2
            - 重命名已安装的模组
            ## 9.9.9
            - 不存在的版本
            """);

        var shown = Changelog.Translate(english, chinese);

        Assert.Equal(["1.4.0", "1.3.2"], shown.Select(r => r.Version));
        Assert.Equal(["Loader mods next to the exe"], shown[0].Changes);
        Assert.Equal(["重命名已安装的模组"], shown[1].Changes);
        Assert.Equal(new DateOnly(2026, 11, 1), shown[1].Date);
    }

    /// <summary>A heading with a typo in a translation would leave its lines unused; every one names a real release.</summary>
    [Fact]
    public void Every_translated_release_is_one_of_the_English_ones()
    {
        var english = Changelog.Load().Select(r => r.Version).ToHashSet();

        Assert.NotEmpty(Changelog.Languages());
        foreach (var language in Changelog.Languages())
        {
            var shown = Changelog.Load(language);
            Assert.Equal(english.Count, shown.Count);
            Assert.All(shown, release => Assert.NotEmpty(release.Changes));
            Assert.NotEqual(Changelog.Load()[^1].Changes, shown[^1].Changes);
        }
        Assert.Equal(Changelog.Load().Select(r => r.Changes), Changelog.Load("de").Select(r => r.Changes));
    }

    /// <summary>A release without its section would show an old list: publish.ps1 runs the tests, so it stops then.</summary>
    [Fact]
    public void The_changelog_built_into_the_app_starts_with_this_version()
    {
        var releases = Changelog.Load();

        Assert.Equal(AppInfo.Version, releases[0].Version);
        Assert.All(releases, release => Assert.NotEmpty(release.Changes));
    }
}
