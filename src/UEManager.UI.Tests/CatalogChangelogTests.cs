using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Windows.Threading;
using UEManager.Core.Games;
using UEManager.Core.Storage;
using UEManager.UI.Catalog;
using UEManager.UI.Services;
using UEManager.UI.ViewModels;
using Xunit;

namespace UEManager.UI.Tests;

/// <summary>A card's Changelog button shows the texts of the mod's newest releases on GitHub.</summary>
public sealed class CatalogChangelogTests : IDisposable
{
    private const string GamesUrl = "https://openmods.test/games/mods.json";

    private const string Games = """
        { "openmods": 1, "games": [ { "slug": "test", "name": "Test", "steamAppId": "1", "mods": 3, "list": "https://openmods.test/games/test/mods.json" } ] }
        """;

    private const string Mods = """
        { "openmods": 1, "mods": [
          { "name": "Map", "source": "https://github.com/KintaroModding/map" },
          { "name": "Fresh", "source": "https://github.com/KintaroModding/fresh" },
          { "name": "Elsewhere", "source": "https://gitlab.com/someone/elsewhere" }
        ] }
        """;

    private const string MapReleases = """
        [ { "tag_name": "v1.2", "published_at": "2026-11-15T10:00:00Z", "body": "## Added\n- Outlines for **every** region" },
          { "tag_name": "v1.1", "published_at": "2026-11-14T10:00:00Z", "body": "" } ]
        """;

    private readonly string folder = Path.Combine(Path.GetTempPath(), "uemm-changelog-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(folder))
            Directory.Delete(folder, recursive: true);
    }

    /// <summary>Answers known URLs with their text, every other URL with 404; GitHub can be down, and counts the questions.</summary>
    private sealed class Sites(Dictionary<string, string> pages) : HttpMessageHandler
    {
        public bool GitHubDown { get; set; }

        public int GitHubAsked { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.AbsoluteUri;
            if (url.StartsWith("https://api.github.com/", StringComparison.Ordinal))
            {
                GitHubAsked++;
                if (GitHubDown)
                    throw new HttpRequestException("No such host is known.");
            }
            return Task.FromResult(pages.TryGetValue(url, out var text)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(text, Encoding.UTF8) }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    [Fact]
    public void The_changelog_shows_the_newest_releases_and_says_why_when_it_cannot() => UiThread.Run(() =>
    {
        var paths = new AppPaths(Path.Combine(folder, "Data"));
        paths.EnsureWritable();
        var model = new MainViewModel(new GameProfile { Id = "test", DisplayName = "Test" }, paths, new ArtSet(null, null));
        var sites = new Sites(new()
        {
            [GamesUrl] = Games,
            ["https://openmods.test/games/test/mods.json"] = Mods,
            ["https://api.github.com/repos/kintaromodding/map/releases?per_page=10"] = MapReleases,
            ["https://api.github.com/repos/kintaromodding/fresh/releases?per_page=10"] = "[]",
        });
        var catalog = new CatalogViewModel(model, new HttpClient(sites), GamesUrl, "1", isTest: false, CatalogSortField.Name, false);
        WaitFor(catalog.LoadAsync());
        var cards = catalog.Items.ToDictionary(item => item.Name);

        // Only a source on GitHub has releases to show.
        Assert.True(cards["Map"].HasChangelog);
        Assert.False(cards["Elsewhere"].HasChangelog);
        catalog.ShowChangelogCommand.Execute(cards["Elsewhere"]);
        Assert.Null(catalog.Changelog);

        // GitHub down: the reason, and Try again asks once more.
        sites.GitHubDown = true;
        catalog.ShowChangelogCommand.Execute(cards["Map"]);
        var changelog = catalog.Changelog!;
        Assert.Equal("Map", changelog.Name);
        WaitFor(changelog.Loaded);
        Assert.True(changelog.HasError);
        Assert.Equal("GitHub could not be reached: No such host is known.", changelog.Error);
        Assert.False(changelog.IsEmpty);

        sites.GitHubDown = false;
        changelog.RetryCommand.Execute(null);
        WaitFor(changelog.Loaded);
        Assert.False(changelog.HasError);
        Assert.Equal(["1.2", "1.1"], changelog.Releases.Select(release => release.Version));
        var newest = changelog.Releases[0];
        Assert.Equal(["Added", "Outlines for every region"], newest.Blocks.Select(block => block.Text));
        Assert.True(newest.Blocks[0].IsHeading);
        Assert.True(newest.Blocks[1].IsBullet);
        Assert.True(changelog.Releases[1].HasNoText);
        Assert.Equal(2, sites.GitHubAsked);

        // Opened again in this run: the answer GitHub gave, without asking again.
        changelog.CloseCommand.Execute(null);
        Assert.Null(catalog.Changelog);
        catalog.ShowChangelogCommand.Execute(cards["Map"]);
        WaitFor(catalog.Changelog!.Loaded);
        Assert.Equal(2, catalog.Changelog.Releases.Count);
        Assert.Equal(2, sites.GitHubAsked);

        // No release yet.
        catalog.ShowChangelogCommand.Execute(cards["Fresh"]);
        WaitFor(catalog.Changelog!.Loaded);
        Assert.True(catalog.Changelog.IsEmpty);
    });

    private static void WaitFor(Task task)
    {
        var frame = new DispatcherFrame();
        var dispatcher = Dispatcher.CurrentDispatcher;
        _ = task.ContinueWith(_ => dispatcher.BeginInvoke(() => frame.Continue = false), TaskScheduler.Default);
        Dispatcher.PushFrame(frame);
        task.GetAwaiter().GetResult();
    }
}
