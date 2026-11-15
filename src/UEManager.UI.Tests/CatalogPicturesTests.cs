using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using UEManager.Core.Catalog;
using UEManager.Core.Games;
using UEManager.Core.Storage;
using UEManager.UI.Catalog;
using UEManager.UI.Services;
using UEManager.UI.ViewModels;
using Xunit;

namespace UEManager.UI.Tests;

/// <summary>A click on an openmods card's cover shows the mod's cover and screenshots in the picture viewer.</summary>
public sealed class CatalogPicturesTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "uemm-pictures-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(folder))
            Directory.Delete(folder, recursive: true);
    }

    /// <summary>Loads every picture but the ones whose address says "missing", and notes each address and width asked.</summary>
    private sealed class FakePictures
    {
        public List<(string Url, int Width)> Asked { get; } = [];

        public Task<ImageSource?> LoadAsync(string url, int width)
        {
            lock (Asked)
                Asked.Add((url, width));
            if (url.Contains("missing", StringComparison.Ordinal))
                return Task.FromResult<ImageSource?>(null);
            var image = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[4], 4);
            image.Freeze();
            return Task.FromResult<ImageSource?>(image);
        }

        public string[] At(int width)
        {
            lock (Asked)
                return Asked.Where(asked => asked.Width == width).Select(asked => asked.Url).Order().ToArray();
        }
    }

    private static readonly CatalogPicture[] Three =
    [
        new("https://e.test/cover.png", null),
        new("https://i.imgur.com/missing.png", "Gone from its site"),
        new("https://e.test/shot.png", "The map"),
    ];

    [Fact]
    public void The_viewer_shows_one_picture_at_a_time_and_steps_round() => UiThread.Run(() =>
    {
        var pictures = new FakePictures();
        var closed = 0;
        var viewer = new CatalogPicturesViewModel("World Map Outlines", Three, pictures.LoadAsync, () => closed++);
        WaitFor(viewer.Thumbnails);

        Assert.Equal("World Map Outlines", viewer.Name);
        Assert.True(viewer.HasMany);
        Assert.Equal("1 / 3", viewer.CounterText);
        Assert.Equal([true, false, false], viewer.Pictures.Select(p => p.IsCurrent));
        // Every thumbnail, and the picture with its neighbors on both sides, so a step shows the next one at once.
        Assert.Equal(Three.Select(p => p.Url).Order(), pictures.At(CatalogPicturesViewModel.ThumbnailWidth));
        Assert.Equal(Three.Select(p => p.Url).Order(), pictures.At(CatalogPicturesViewModel.PictureWidth));
        Assert.NotNull(viewer.Current.Image);
        Assert.False(viewer.Current.IsLoading);

        // A picture that does not load gets a placeholder; the others stay.
        viewer.NextCommand.Execute(null);
        Assert.Equal("2 / 3", viewer.CounterText);
        Assert.True(viewer.Current.Failed);
        Assert.True(viewer.Current.ThumbnailFailed);
        Assert.Equal("Gone from its site", viewer.Current.Alt);

        viewer.NextCommand.Execute(null);
        viewer.NextCommand.Execute(null);
        Assert.Equal("1 / 3", viewer.CounterText);
        viewer.PreviousCommand.Execute(null);
        Assert.Equal("3 / 3", viewer.CounterText);
        Assert.Equal("The map", viewer.Current.Alt);

        viewer.SelectCommand.Execute(viewer.Pictures[0]);
        Assert.Equal("1 / 3", viewer.CounterText);
        Assert.False(viewer.Current.HasAlt);

        // Each picture is loaded once, however often it shows.
        Assert.Equal(3, pictures.At(CatalogPicturesViewModel.PictureWidth).Length);

        viewer.CloseCommand.Execute(null);
        Assert.Equal(1, closed);
    });

    /// <summary>Loads thumbnails after a short wait, so several are under way at once, and notes the most at a time.</summary>
    private sealed class SlowThumbnails
    {
        private readonly FakePictures pictures = new();
        private int running;

        public int MostAtOnce { get; private set; }

        public FakePictures Asked => pictures;

        public async Task<ImageSource?> LoadAsync(string url, int width)
        {
            if (width != CatalogPicturesViewModel.ThumbnailWidth)
                return await pictures.LoadAsync(url, width);
            MostAtOnce = Math.Max(MostAtOnce, ++running);
            await Task.Delay(5);
            running--;
            return await pictures.LoadAsync(url, width);
        }
    }

    [Fact]
    public void Twenty_screenshots_load_the_shown_picture_first_and_keep_few_large_ones() => UiThread.Run(() =>
    {
        var many = Enumerable.Range(0, 21).Select(i => new CatalogPicture($"https://e.test/{i:00}.png", null)).ToArray();
        var pictures = new SlowThumbnails();
        var viewer = new CatalogPicturesViewModel("Many", many, pictures.LoadAsync, () => { });

        // The shown picture and its two neighbors, round the ends.
        Assert.Equal(["https://e.test/00.png", "https://e.test/01.png", "https://e.test/20.png"],
            pictures.Asked.At(CatalogPicturesViewModel.PictureWidth));
        WaitFor(viewer.Thumbnails);
        Assert.Equal(21, pictures.Asked.At(CatalogPicturesViewModel.ThumbnailWidth).Length);
        Assert.InRange(pictures.MostAtOnce, 1, CatalogPicturesViewModel.ThumbnailsAtOnce);
        Assert.All(viewer.Pictures, picture => Assert.NotNull(picture.Thumbnail));

        // Far from the start, the large pictures there are released; at most five stay.
        viewer.SelectCommand.Execute(viewer.Pictures[10]);
        WaitFor(viewer.CurrentLoading);
        Assert.Equal("11 / 21", viewer.CounterText);
        Assert.Null(viewer.Pictures[0].Image);
        Assert.Null(viewer.Pictures[20].Image);
        Assert.NotNull(viewer.Pictures[9].Image);
        Assert.NotNull(viewer.Pictures[11].Image);
        Assert.InRange(viewer.Pictures.Count(picture => picture.Image is not null), 3, 2 * CatalogPicturesViewModel.KeptAround + 1);

        // Back at the start, the released picture comes back.
        viewer.SelectCommand.Execute(viewer.Pictures[0]);
        WaitFor(viewer.CurrentLoading);
        Assert.NotNull(viewer.Current.Image);
        Assert.False(viewer.Current.IsLoading);
    });

    [Fact]
    public void Pictures_keep_no_download_in_memory_once_shown() => UiThread.Run(() =>
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(4, 4, 96, 96, PixelFormats.Bgra32, null, new byte[64], 16)));
        using var png = new MemoryStream();
        encoder.Save(png);
        var http = new HttpClient(new Files(new() { ["https://e.test/cover.png"] = png.ToArray() }));
        var images = new RemoteImages(http, Path.Combine(folder, "cache"));

        // The card's cover and the viewer's picture at once: one download, then nothing held but the small picture.
        var small = images.LoadAsync("https://e.test/cover.png", 240);
        var large = images.LoadAsync("https://e.test/cover.png", 1920);
        WaitFor(Task.WhenAll(small, large));
        Assert.NotNull(small.Result);
        Assert.NotNull(large.Result);
        Assert.Equal(0, images.Fetching);
        Assert.Same(small.Result, images.LoadAsync("https://e.test/cover.png", 240).Result);
    });

    /// <summary>Answers known URLs with their bytes, every other URL with 404.</summary>
    private sealed class Files(Dictionary<string, byte[]> files) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(files.TryGetValue(request.RequestUri!.AbsoluteUri, out var bytes)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    [Fact]
    public void A_single_picture_needs_no_arrows_and_no_thumbnails() => UiThread.Run(() =>
    {
        var pictures = new FakePictures();
        var viewer = new CatalogPicturesViewModel("Cover only", [new("https://e.test/cover.png", null)], pictures.LoadAsync, () => { });
        WaitFor(viewer.CurrentLoading);

        Assert.False(viewer.HasMany);
        Assert.Empty(pictures.At(CatalogPicturesViewModel.ThumbnailWidth));
        Assert.Equal(["https://e.test/cover.png"], pictures.At(CatalogPicturesViewModel.PictureWidth));
        viewer.NextCommand.Execute(null);
        Assert.Equal(0, viewer.Index);
    });

    private const string GamesUrl = "https://openmods.test/games/mods.json";

    private const string Games = """
        { "openmods": 1, "games": [ { "slug": "test", "name": "Test", "steamAppId": "1", "mods": 3, "list": "https://openmods.test/games/test/mods.json" } ] }
        """;

    private const string Mods = """
        { "openmods": 1, "game": { "name": "Test", "art": "https://openmods.test/art.png" }, "mods": [
          { "name": "Alpha", "source": "https://github.com/A/alpha", "cover": "https://e.test/alpha.png",
            "screenshots": [ { "url": "https://e.test/alpha-1.png", "alt": "One" }, { "url": "https://i.imgur.com/alpha-2.png" } ] },
          { "name": "Beta", "source": "https://github.com/A/beta", "cover": "https://e.test/beta.png" },
          { "name": "Gamma", "source": "https://github.com/A/gamma" }
        ] }
        """;

    [Fact]
    public void A_click_on_a_cover_opens_the_viewer_with_the_cover_first() => UiThread.Run(() =>
    {
        var paths = new AppPaths(Path.Combine(folder, "Data"));
        paths.EnsureWritable();
        var model = new MainViewModel(new GameProfile { Id = "test", DisplayName = "Test" }, paths, new ArtSet(null, null));
        var http = new HttpClient(new Pages(new() { [GamesUrl] = Games, ["https://openmods.test/games/test/mods.json"] = Mods }));
        var catalog = new CatalogViewModel(model, http, GamesUrl, "1", isTest: false, CatalogSortField.Name, false);
        WaitFor(catalog.LoadAsync());
        var cards = catalog.Items.ToDictionary(item => item.Name);

        // The box on the cover, which invites the click: Gamma shows the game's picture, which is not the mod's.
        Assert.Equal([true, true, false], new[] { "Alpha", "Beta", "Gamma" }.Select(name => cards[name].HasPictures));
        Assert.Equal(3, cards["Alpha"].PictureCount);
        Assert.Equal(1, cards["Beta"].PictureCount);

        catalog.ShowPicturesCommand.Execute(cards["Alpha"]);
        Assert.NotNull(catalog.Pictures);
        Assert.Equal("Alpha", catalog.Pictures!.Name);
        Assert.Equal(["https://e.test/alpha.png", "https://e.test/alpha-1.png", "https://i.imgur.com/alpha-2.png"],
            catalog.Pictures.Pictures.Select(p => p.Url));

        catalog.Pictures.CloseCommand.Execute(null);
        Assert.Null(catalog.Pictures);

        catalog.ShowPicturesCommand.Execute(cards["Gamma"]);
        Assert.Null(catalog.Pictures);
    });

    [Fact]
    public void Settings_clears_the_picture_cache_with_one_click_and_says_how_it_went() => UiThread.Run(() =>
    {
        var paths = new AppPaths(Path.Combine(folder, "Data"));
        paths.EnsureWritable();
        var model = new MainViewModel(new GameProfile { Id = "test", DisplayName = "Test" }, paths, new ArtSet(null, null));

        model.ClearPictureCacheCommand.Execute(null);
        Assert.Equal("The picture cache is empty.", model.Toast?.Text);
        Assert.False(model.Toast!.IsError);

        Directory.CreateDirectory(paths.PictureCacheDir);
        File.WriteAllBytes(Path.Combine(paths.PictureCacheDir, "a.bin"), new byte[10]);
        File.WriteAllText(Path.Combine(paths.PictureCacheDir, "a.json"), "{}");
        var held = Path.Combine(paths.PictureCacheDir, "b.bin");
        File.WriteAllBytes(held, new byte[30]);

        // Another program holds a picture: the message says so, in red.
        using (new FileStream(held, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            model.ClearPictureCacheCommand.Execute(null);
            Assert.True(model.Toast!.IsError);
            Assert.StartsWith("The picture cache could not be cleared: ", model.Toast.Text);
        }

        File.WriteAllBytes(Path.Combine(paths.PictureCacheDir, "a.bin"), new byte[10]);
        model.ClearPictureCacheCommand.Execute(null);
        Assert.False(model.Toast!.IsError);
        Assert.StartsWith("Picture cache cleared: 2 pictures, ", model.Toast.Text);
        Assert.Empty(Directory.GetFiles(paths.PictureCacheDir));
    });

    private static void WaitFor(Task task)
    {
        var frame = new DispatcherFrame();
        var dispatcher = Dispatcher.CurrentDispatcher;
        _ = task.ContinueWith(_ => dispatcher.BeginInvoke(() => frame.Continue = false), TaskScheduler.Default);
        Dispatcher.PushFrame(frame);
        task.GetAwaiter().GetResult();
    }

    /// <summary>Answers known URLs with their text, every other URL with 404.</summary>
    private sealed class Pages(Dictionary<string, string> pages) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(pages.TryGetValue(request.RequestUri!.AbsoluteUri, out var text)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(text, Encoding.UTF8) }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}
