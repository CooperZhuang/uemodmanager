using System.IO;
using System.Net.Http;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using UEManager.Core;
using UEManager.Core.Catalog;

namespace UEManager.UI.Catalog;

/// <summary>
/// Pictures from the internet, such as catalog covers and screenshots. They are downloaded with the app's own
/// connection and kept in the data folder (<see cref="PictureCache"/>), so each is downloaded once: nothing is written
/// outside the Data folder, as a WPF image with a web address would do through the Windows internet cache.
/// </summary>
internal sealed class RemoteImages(HttpClient http, string cacheFolder)
{
    /// <summary>
    /// The widest picture kept in memory for the run: the covers and the viewer's thumbnails. The viewer's large pictures,
    /// several MB each when decoded, are kept by the viewer only while it shows them or their neighbors.
    /// </summary>
    internal const int KeptWidth = 480;

    private readonly PictureCache files = new(cacheFolder, http);

    /// <summary>
    /// The downloads under way, so a picture asked at two widths at once is fetched once. A finished one leaves: its
    /// bytes, a few MB per picture, are not kept in memory; the copy on disk serves the next width.
    /// </summary>
    private readonly Dictionary<string, Task<byte[]?>> fetching = new(StringComparer.Ordinal);

    private readonly Dictionary<string, Task<ImageSource?>> images = new(StringComparer.Ordinal);

    /// <summary>The downloads under way. Tests check that none is left.</summary>
    internal int Fetching => fetching.Count;

    /// <param name="url">An https address; anything else gives null.</param>
    /// <param name="width">
    /// The width to decode to, in pixels, which keeps large pictures small in memory. Each width is kept on its own: a
    /// card's cover and the same picture in the picture viewer differ. Wider than <see cref="KeptWidth"/>, it is not kept.
    /// </param>
    public Task<ImageSource?> LoadAsync(string? url, int width = 480)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            return Task.FromResult<ImageSource?>(null);
        if (width > KeptWidth)
            return DecodeAsync(uri, width);
        var key = $"{width} {uri.AbsoluteUri}";
        if (!images.TryGetValue(key, out var image))
            images[key] = image = DecodeAsync(uri, width);
        return image;
    }

    private Task<byte[]?> FetchAsync(Uri uri)
    {
        if (fetching.TryGetValue(uri.AbsoluteUri, out var running))
            return running;
        var fetch = FetchAndForgetAsync(uri);
        // A copy that needed no wait is done already, and its forgetting ran before it could be noted.
        if (!fetch.IsCompleted)
            fetching[uri.AbsoluteUri] = fetch;
        return fetch;
    }

    private async Task<byte[]?> FetchAndForgetAsync(Uri uri)
    {
        try
        {
            return await files.GetAsync(uri);
        }
        finally
        {
            fetching.Remove(uri.AbsoluteUri);
        }
    }

    private async Task<ImageSource?> DecodeAsync(Uri uri, int width)
    {
        if (await FetchAsync(uri) is not { } data)
            return null;
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            // Never wider than the picture is: a small screenshot stays sharp in the viewer.
            image.DecodePixelWidth = Math.Min(width, PixelWidth(data) ?? width);
            image.StreamSource = new MemoryStream(data);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or IOException or InvalidOperationException
                                       or ArgumentException)
        {
            Log.Warn($"Picture '{uri}' could not be read: {ex.Message}");
            return null;
        }
    }

    /// <summary>The picture's width in pixels, from its header only; null when it cannot be read.</summary>
    private static int? PixelWidth(byte[] bytes)
    {
        try
        {
            using var stream = new MemoryStream(bytes);
            var frame = BitmapFrame.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            return frame.PixelWidth > 0 ? frame.PixelWidth : null;
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or IOException or InvalidOperationException
                                       or ArgumentException)
        {
            return null;
        }
    }
}
