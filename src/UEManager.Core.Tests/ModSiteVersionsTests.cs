using System.Net;
using System.Text;
using System.Text.Json;
using UEManager.Core.Updates;

namespace UEManager.Core.Tests;

/// <summary>The update check: one request for the page version of many mods, and how a version compares with it.</summary>
public class ModSiteVersionsTests
{
    private const string Api = "https://api.example.test/v2/graphql";

    /// <summary>A fake mod site that records each request and answers with the given text.</summary>
    private sealed class Site(Func<int, string> answer, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public List<(string Body, HttpRequestMessage Request)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((await request.Content!.ReadAsStringAsync(cancellationToken), request));
            return new HttpResponseMessage(status) { Content = new StringContent(answer(Requests.Count), Encoding.UTF8, "application/json") };
        }
    }

    private static string Nodes(params (object Id, string Version, string Status)[] mods) => JsonSerializer.Serialize(new
    {
        data = new { mods = new { nodes = mods.Select(m => new { modId = m.Id, version = m.Version, status = m.Status }) } },
    });

    private static Task<IReadOnlyDictionary<string, string>?> Fetch(Site site, params string[] ids) =>
        ModSiteVersions.FetchAsync(new HttpClient(site), Api, "10614", ids, "1.6.5", TimeSpan.FromSeconds(5));

    [Fact]
    public async Task One_request_asks_for_every_mod_with_the_game_and_names_the_app()
    {
        var site = new Site(_ => Nodes((2, "1.6.4", "published"), (149, "0.9.3", "published")));

        var found = await Fetch(site, "2", "149");

        Assert.Equal(new Dictionary<string, string> { ["2"] = "1.6.4", ["149"] = "0.9.3" }, found);
        var (body, request) = Assert.Single(site.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("UE MM", request.Headers.GetValues("Application-Name").Single());
        Assert.Equal("1.6.5", request.Headers.GetValues("Application-Version").Single());
        // One filter per mod, joined by OR, each with the game: the site's API needs the game with every mod number.
        using var json = JsonDocument.Parse(body);
        var filter = json.RootElement.GetProperty("variables").GetProperty("filter");
        Assert.Equal("OR", filter.GetProperty("op").GetString());
        var parts = filter.GetProperty("filter").EnumerateArray().ToList();
        Assert.Equal(["2", "149"], parts.Select(p => p.GetProperty("modId")[0].GetProperty("value").GetString()));
        Assert.All(parts, p => Assert.Equal("10614", p.GetProperty("gameId")[0].GetProperty("value").GetString()));
        Assert.Equal(2, json.RootElement.GetProperty("variables").GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task Many_mods_go_in_parts_of_fifty()
    {
        var site = new Site(_ => Nodes());

        await Fetch(site, Enumerable.Range(1, 120).Select(i => i.ToString()).ToArray());

        Assert.Equal(3, site.Requests.Count);
    }

    [Fact]
    public async Task A_page_that_is_not_published_has_no_version()
    {
        var site = new Site(_ => Nodes((2, "1.6.4", "published"), (7, "2.0", "hidden"), ("8", "1.1", "published")));

        var found = await Fetch(site, "2", "7", "8");

        Assert.Equal(new Dictionary<string, string> { ["2"] = "1.6.4", ["8"] = "1.1" }, found);
    }

    [Theory]
    [InlineData("""{"errors":[{"message":"gameId is required when filtering by modId"}],"data":null}""")]
    [InlineData("""{"data":{"other":1}}""")]
    [InlineData("not json")]
    public async Task An_error_or_an_unknown_answer_is_no_answer(string answer)
    {
        Assert.Null(await Fetch(new Site(_ => answer), "2"));
    }

    [Fact]
    public async Task A_failed_request_is_no_answer()
    {
        Assert.Null(await Fetch(new Site(_ => "{}", HttpStatusCode.TooManyRequests), "2"));
    }

    [Fact]
    public async Task No_mods_send_nothing()
    {
        var site = new Site(_ => Nodes());

        Assert.Empty((await Fetch(site))!);
        Assert.Empty(site.Requests);
    }

    [Theory]
    [InlineData("1.7.2", "1.7.3", ModSiteState.UpdateAvailable)]
    [InlineData("1.7.2", "1.10", ModSiteState.UpdateAvailable)]
    [InlineData("1.6", "v1.6.1", ModSiteState.UpdateAvailable)]
    [InlineData("1.7.2", "1.7.2", ModSiteState.UpToDate)]
    [InlineData("1.6", "1.6.0", ModSiteState.UpToDate)]
    [InlineData("v2.0", "2.0", ModSiteState.UpToDate)]
    // Newer than the page, as the Quest Guide's file number 10 against its page 1.2.4: nothing newer is there.
    [InlineData("10", "1.2.4", ModSiteState.UpToDate)]
    // Text after the numbers: the numbers tell, as PhantomHUD withHP's page 0.5.2.withHP against its file 0.5.1 (#22).
    [InlineData("0.5.1", "0.5.2.withHP", ModSiteState.UpdateAvailable)]
    [InlineData("0.5.3", "0.5.2.withHP", ModSiteState.UpToDate)]
    [InlineData("1.2-beta", "1.3", ModSiteState.UpdateAvailable)]
    [InlineData("v1.1", "1.2a", ModSiteState.UpdateAvailable)]
    // The same numbers: a single letter right after them counts in the alphabet's order, as a fix.
    [InlineData("1.2a", "1.2b", ModSiteState.UpdateAvailable)]
    [InlineData("1.2a", "1.2B", ModSiteState.UpdateAvailable)]
    [InlineData("1.2", "1.2a", ModSiteState.UpdateAvailable)]
    [InlineData("1.2b", "1.2a", ModSiteState.UpToDate)]
    [InlineData("1.2a", "1.2", ModSiteState.UpToDate)]
    [InlineData("1.2.0a", "1.2a", ModSiteState.UpToDate)]
    // The same numbers with any other text: only the same text tells.
    [InlineData("0.5.2.withHP", "0.5.2.withHP", ModSiteState.UpToDate)]
    [InlineData("1.2-beta", "1.2-BETA", ModSiteState.UpToDate)]
    [InlineData("0.5.2", "0.5.2.withHP", ModSiteState.Unknown)]
    [InlineData("1.2-beta", "1.2", ModSiteState.Unknown)]
    [InlineData("1.2-a", "1.2-b", ModSiteState.Unknown)]
    [InlineData("1.2a", "1.2ab", ModSiteState.Unknown)]
    // No number at the start: only the same text tells.
    [InlineData("beta", "beta", ModSiteState.UpToDate)]
    [InlineData("beta", "1.0", ModSiteState.Unknown)]
    [InlineData("1.0", "Final", ModSiteState.Unknown)]
    [InlineData("1.0", "", ModSiteState.Unknown)]
    [InlineData(null, "1.0", ModSiteState.Unknown)]
    public void The_installed_version_compares_with_the_page(string? installed, string page, ModSiteState state)
    {
        Assert.Equal(state, ModSiteVersions.Compare(installed, page));
    }
}
