using System.Net;
using System.Net.Http.Headers;
using UEManager.Core.Catalog;

namespace UEManager.Core.Tests;

/// <summary>The openmods page's pictures are downloaded once and kept in the data folder.</summary>
public sealed class PictureCacheTests : IDisposable
{
    private readonly TestFolder folder = new();
    private DateTimeOffset now = new(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);

    public void Dispose() => folder.Dispose();

    private string Cache => folder.Combine("cache");

    private static readonly Uri Cover = new("https://raw.githubusercontent.com/A/map/main/docs/cover.png");

    /// <summary>A site that answers with the picture's bytes and ETag, or "not changed" for the ETag it was asked with.</summary>
    private sealed class Site : HttpMessageHandler
    {
        public Dictionary<string, (byte[] Bytes, string ETag)> Pictures { get; } = [];

        public List<(string Url, string? IfNoneMatch)> Asked { get; } = [];

        public bool Down { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.AbsoluteUri;
            var ifNoneMatch = request.Headers.TryGetValues("If-None-Match", out var values) ? values.Single() : null;
            Asked.Add((url, ifNoneMatch));
            if (Down)
                throw new HttpRequestException("No such host is known.");
            if (!Pictures.TryGetValue(url, out var picture))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            if (ifNoneMatch == picture.ETag)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotModified));
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(picture.Bytes) };
            response.Headers.ETag = new EntityTagHeaderValue(picture.ETag);
            return Task.FromResult(response);
        }
    }

    private PictureCache With(Site site, long maxBytes = PictureCache.DefaultMaxBytes) =>
        new(Cache, new HttpClient(site), maxBytes, TimeSpan.FromHours(1), () => now);

    [Fact]
    public async Task A_picture_is_downloaded_once_and_then_checked_with_its_etag()
    {
        var site = new Site { Pictures = { [Cover.AbsoluteUri] = ("cover one"u8.ToArray(), "\"one\"") } };
        var cache = With(site);

        Assert.Equal("cover one"u8.ToArray(), await cache.GetAsync(Cover));
        Assert.Single(Directory.GetFiles(Cache, "*.bin"));

        // Within the hour: the copy, without asking the site; also for a new run of the app.
        now = now.AddMinutes(30);
        Assert.Equal("cover one"u8.ToArray(), await With(site).GetAsync(Cover));
        Assert.Single(site.Asked);

        // Later: asked with the ETag; "not changed" carries no picture, and the copy is used.
        now = now.AddHours(2);
        Assert.Equal("cover one"u8.ToArray(), await With(site).GetAsync(Cover));
        Assert.Equal("\"one\"", site.Asked[^1].IfNoneMatch);

        // A new picture at the same address replaces the copy.
        site.Pictures[Cover.AbsoluteUri] = ("cover two"u8.ToArray(), "\"two\"");
        now = now.AddHours(2);
        Assert.Equal("cover two"u8.ToArray(), await With(site).GetAsync(Cover));
        now = now.AddMinutes(10);
        Assert.Equal("cover two"u8.ToArray(), await With(site).GetAsync(Cover));
        // Asked three times: the download, the check with the ETag, and the new picture.
        Assert.Equal(3, site.Asked.Count);
    }

    [Fact]
    public async Task The_copy_is_shown_when_the_site_cannot_be_reached()
    {
        var site = new Site { Pictures = { [Cover.AbsoluteUri] = ("cover"u8.ToArray(), "\"one\"") } };
        await With(site).GetAsync(Cover);

        now = now.AddDays(3);
        site.Down = true;
        Assert.Equal("cover"u8.ToArray(), await With(site).GetAsync(Cover));

        // Gone from the site: the copy too; without a copy, nothing.
        site.Down = false;
        site.Pictures.Clear();
        Assert.Equal("cover"u8.ToArray(), await With(site).GetAsync(Cover));
        Assert.Null(await With(site).GetAsync(new Uri("https://i.imgur.com/never.png")));
        site.Down = true;
        Assert.Null(await With(site).GetAsync(new Uri("https://i.imgur.com/never-either.png")));
    }

    [Fact]
    public async Task A_file_larger_than_a_picture_is_not_kept()
    {
        var huge = new byte[PictureCache.MaxPictureBytes + 1];
        var site = new Site { Pictures = { [Cover.AbsoluteUri] = (huge, "\"huge\"") } };

        Assert.Null(await With(site).GetAsync(Cover));
        Assert.False(Directory.Exists(Cache) && Directory.GetFiles(Cache).Length > 0);
    }

    [Fact]
    public async Task The_pictures_used_longest_ago_go_first_beyond_the_limit()
    {
        var site = new Site();
        var urls = new[] { "https://e.test/a.png", "https://e.test/b.png", "https://e.test/c.png" };
        foreach (var url in urls)
            site.Pictures[url] = (new byte[10], $"\"{url[^5]}\"");
        var cache = With(site, maxBytes: 25);

        await cache.GetAsync(new Uri(urls[0]));
        now = now.AddMinutes(1);
        await cache.GetAsync(new Uri(urls[1]));
        // a is used again, so b is now the one used longest ago.
        now = now.AddMinutes(1);
        await cache.GetAsync(new Uri(urls[0]));
        now = now.AddMinutes(1);
        await cache.GetAsync(new Uri(urls[2]));

        Assert.Equal(2, Directory.GetFiles(Cache, "*.bin").Length);
        site.Down = true;
        Assert.NotNull(await cache.GetAsync(new Uri(urls[0])));
        Assert.NotNull(await cache.GetAsync(new Uri(urls[2])));
        Assert.Null(await cache.GetAsync(new Uri(urls[1])));
    }

    [Fact]
    public async Task Clear_removes_every_copy_and_says_how_much()
    {
        Assert.Equal((0, 0L), PictureCache.Clear(Cache));

        var site = new Site
        {
            Pictures =
            {
                ["https://e.test/a.png"] = (new byte[10], "\"a\""),
                ["https://e.test/b.png"] = (new byte[30], "\"b\""),
            },
        };
        await With(site).GetAsync(new Uri("https://e.test/a.png"));
        await With(site).GetAsync(new Uri("https://e.test/b.png"));

        Assert.Equal((2, 40L), PictureCache.Clear(Cache));
        Assert.Empty(Directory.GetFiles(Cache));
        Assert.Equal((0, 0L), PictureCache.Clear(Cache));
    }
}
