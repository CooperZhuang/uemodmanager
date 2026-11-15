using System.Net;
using System.Text;
using UEManager.Core.Catalog;
using UEManager.Core.Mods;

namespace UEManager.Core.Tests;

/// <summary>The changelog of an openmods mod: the texts of its releases on GitHub, read on a click.</summary>
public class ModReleasesTests
{
    private const string Releases = """
        [
          { "tag_name": "v1.2", "name": "Animation Cancel 1.2", "published_at": "2026-11-14T10:00:00Z", "draft": false,
            "prerelease": false, "html_url": "https://github.com/A/mod/releases/tag/v1.2", "body": "## Fixed\r\n- The dodge" },
          { "tag_name": "v1.3-beta", "published_at": "2026-11-15T10:00:00Z", "prerelease": true, "body": null,
            "html_url": "https://evil.test/page" },
          { "tag_name": "v9", "draft": true, "body": "not shown" },
          { "name": "No tag" }
        ]
        """;

    [Fact]
    public void Reads_the_releases_without_drafts()
    {
        var notes = ModReleases.Parse(Releases);

        Assert.Equal(["v1.2", "v1.3-beta"], notes.Select(n => n.Tag));
        Assert.Equal("1.2", notes[0].Version);
        Assert.Equal("Animation Cancel 1.2", notes[0].Name);
        Assert.Equal(new DateTimeOffset(2026, 11, 14, 10, 0, 0, TimeSpan.Zero), notes[0].Date);
        Assert.Equal("## Fixed\r\n- The dodge", notes[0].Body);
        Assert.False(notes[0].IsPreRelease);
        Assert.True(notes[1].IsPreRelease);
        Assert.Equal("", notes[1].Body);
        // Only GitHub's own pages are kept as links.
        Assert.Equal("https://github.com/A/mod/releases/tag/v1.2", notes[0].Url);
        Assert.Null(notes[1].Url);
    }

    [Theory]
    [InlineData("https://github.com/KintaroModding/bitfix", "kintaromodding/bitfix")]
    [InlineData("https://github.com/Someone/Repack.git", "someone/repack")]
    [InlineData("https://gitlab.com/a/b", null)]
    [InlineData("http://github.com/a/b", null)]
    [InlineData(null, null)]
    public void Only_github_sources_have_a_changelog(string? source, string? repo) => Assert.Equal(repo, ModReleases.RepoOf(source));

    [Fact]
    public void A_release_text_becomes_plain_blocks()
    {
        const string Markdown = """
            # What's new in **1.2**
            Now with a *faster* dodge, see [the page](https://github.com/A/mod) and `config.lua`.
            It also runs on UE4SS v4.

            ![shot](https://i.imgur.com/x.png)
            ## Fixed
            - A crash at the **main menu**
              when the game starts
            - Settings:
              * `Speed` \*kept\*
            1. First
            2) Second
            ---
            > A note <br> from the author
            ```lua
            local x = 1
              return x
            ```
            <details><summary>More</summary>Hidden &amp; shown</details>
            """;

        Assert.Equal(
        [
            new NoteBlock(NoteBlockKind.Heading, "What's new in 1.2", 1),
            new NoteBlock(NoteBlockKind.Paragraph, "Now with a faster dodge, see the page and config.lua. It also runs on UE4SS v4."),
            new NoteBlock(NoteBlockKind.Heading, "Fixed", 2),
            new NoteBlock(NoteBlockKind.Bullet, "A crash at the main menu when the game starts"),
            new NoteBlock(NoteBlockKind.Bullet, "Settings:"),
            new NoteBlock(NoteBlockKind.Bullet, "Speed *kept*", 1),
            new NoteBlock(NoteBlockKind.Bullet, "First", 0, "1."),
            new NoteBlock(NoteBlockKind.Bullet, "Second", 0, "2)"),
            new NoteBlock(NoteBlockKind.Paragraph, "A note from the author"),
            new NoteBlock(NoteBlockKind.Code, "local x = 1\n  return x"),
            new NoteBlock(NoteBlockKind.Paragraph, "More Hidden & shown"),
        ], ModReleases.Blocks(Markdown));
        Assert.Empty(ModReleases.Blocks(""));
        Assert.Empty(ModReleases.Blocks("![only a picture](https://e.test/a.png)"));
    }

    /// <summary>Answers every request with one status, body and headers.</summary>
    private sealed class GitHub(HttpStatusCode status, string body = "[]", (string, string)? header = null, bool down = false) : HttpMessageHandler
    {
        public string? Asked { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Asked = request.RequestUri!.AbsoluteUri;
            if (down)
                throw new HttpRequestException("No such host is known.");
            var response = new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            if (header is var (name, value))
                response.Headers.TryAddWithoutValidation(name, value);
            return Task.FromResult(response);
        }
    }

    [Fact]
    public async Task Fetches_the_newest_releases_of_the_repository()
    {
        var github = new GitHub(HttpStatusCode.OK, Releases);

        var notes = await ModReleases.FetchAsync(new HttpClient(github), "kintaromodding/bitfix");

        Assert.Equal("https://api.github.com/repos/kintaromodding/bitfix/releases?per_page=10", github.Asked);
        Assert.Equal(2, notes.Count);
        Assert.Equal("https://github.com/kintaromodding/bitfix/releases", ModReleases.ReleasesPage("kintaromodding/bitfix"));
    }

    [Fact]
    public async Task Says_why_there_is_no_changelog()
    {
        async Task<string> Error(GitHub github) =>
            (await Assert.ThrowsAsync<ModException>(() => ModReleases.FetchAsync(new HttpClient(github), "a/b"))).Message;

        Assert.Equal("GitHub answers only a few questions an hour from one PC. Try again later.",
            await Error(new GitHub(HttpStatusCode.Forbidden, "{}", ("X-RateLimit-Remaining", "0"))));
        Assert.Equal("GitHub has no releases page for this mod.", await Error(new GitHub(HttpStatusCode.NotFound)));
        Assert.Equal("The changelog could not be loaded: GitHub answered 500.", await Error(new GitHub(HttpStatusCode.InternalServerError)));
        Assert.Equal("GitHub could not be reached: No such host is known.", await Error(new GitHub(HttpStatusCode.OK, down: true)));
        Assert.Equal("GitHub's answer could not be read.", await Error(new GitHub(HttpStatusCode.OK, "{ \"message\": 1 }")));
    }
}
