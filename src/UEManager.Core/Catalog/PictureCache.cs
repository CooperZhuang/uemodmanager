using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using UEManager.Core.Storage;

namespace UEManager.Core.Catalog;

/// <summary>
/// The pictures of the openmods page, its covers and screenshots, kept on disk in the data folder, so the app stays
/// portable and a picture is downloaded once. They are full size, a few MB each. A copy younger than
/// <see cref="DefaultFresh"/> is used without asking; an older one is checked with the site's ETag, whose answer
/// "not changed" carries no picture. When the site cannot be reached, as when it is blocked in the player's country,
/// the copy is shown. The folder stays below a size limit: the pictures used longest ago go first.
/// </summary>
public sealed class PictureCache
{
    /// <summary>The most the folder holds: about 70 full-size covers of 3 MB.</summary>
    public const long DefaultMaxBytes = 200L * 1024 * 1024;

    /// <summary>A larger file is not a cover or a screenshot.</summary>
    public const long MaxPictureBytes = 10L * 1024 * 1024;

    /// <summary>How long a copy is used without asking the site.</summary>
    public static readonly TimeSpan DefaultFresh = TimeSpan.FromHours(1);

    private readonly string folder;
    private readonly HttpClient http;
    private readonly long maxBytes;
    private readonly TimeSpan fresh;
    private readonly Func<DateTimeOffset> now;
    private readonly object pruning = new();

    /// <summary>What is known of a copy, in a small file next to it.</summary>
    /// <param name="Url">The picture's address: a check against a clash of two addresses' file names.</param>
    private sealed record Entry(string Url, string? ETag, string? LastModified, DateTimeOffset FetchedAt);

    /// <param name="folder">The cache's folder in the data folder (<see cref="AppPaths.PictureCacheDir"/>).</param>
    /// <param name="now">The clock; tests give their own.</param>
    public PictureCache(string folder, HttpClient http, long maxBytes = DefaultMaxBytes, TimeSpan? fresh = null, Func<DateTimeOffset>? now = null)
    {
        this.folder = folder;
        this.http = http;
        this.maxBytes = maxBytes;
        this.fresh = fresh ?? DefaultFresh;
        this.now = now ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>The picture's bytes: the copy on disk, or a download that replaces it. Null when there is neither.</summary>
    public async Task<byte[]?> GetAsync(Uri uri, CancellationToken cancellation = default)
    {
        var key = Key(uri);
        var dataPath = Path.Combine(folder, key + ".bin");
        var entryPath = Path.Combine(folder, key + ".json");
        var cached = Read(uri, dataPath, entryPath);
        if (cached is { } young && now() - young.Entry.FetchedAt < fresh)
        {
            Touch(entryPath);
            return young.Bytes;
        }
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            if (cached?.Entry.ETag is { } etag)
                request.Headers.TryAddWithoutValidation("If-None-Match", etag);
            if (cached?.Entry.LastModified is { } modified)
                request.Headers.TryAddWithoutValidation("If-Modified-Since", modified);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotModified && cached is { } same)
            {
                Save(entryPath, same.Entry with { FetchedAt = now() });
                return same.Bytes;
            }
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > MaxPictureBytes)
            {
                Log.Warn($"Picture '{uri}' was not loaded: {(int)response.StatusCode}, {response.Content.Headers.ContentLength} bytes{Stale(cached)}.");
                return cached?.Bytes;
            }
            var bytes = await ReadAtMostAsync(response.Content, timeout.Token).ConfigureAwait(false);
            if (bytes is null)
            {
                Log.Warn($"Picture '{uri}' was not loaded: larger than {MaxPictureBytes} bytes{Stale(cached)}.");
                return cached?.Bytes;
            }
            Store(dataPath, entryPath, new Entry(uri.AbsoluteUri, response.Headers.ETag?.ToString(),
                response.Content.Headers.LastModified?.ToString("R"), now()), bytes);
            return bytes;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or InvalidOperationException)
        {
            Log.Warn($"Picture '{uri}' could not be loaded: {ex.Message}{Stale(cached)}.");
            return cached?.Bytes;
        }
    }

    private static string Stale((Entry Entry, byte[] Bytes)? cached) => cached is null ? "" : "; the copy on disk is shown";

    /// <summary>The copy and what is known of it; null when there is none, or it cannot be read.</summary>
    private static (Entry Entry, byte[] Bytes)? Read(Uri uri, string dataPath, string entryPath)
    {
        try
        {
            if (!File.Exists(dataPath) || JsonFile.Read<Entry>(entryPath) is not { } entry || entry.Url != uri.AbsoluteUri)
                return null;
            return (entry, File.ReadAllBytes(dataPath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <summary>Writes the copy, then makes room. A copy that cannot be written is only missed at the next start.</summary>
    private void Store(string dataPath, string entryPath, Entry entry, byte[] bytes)
    {
        try
        {
            Directory.CreateDirectory(folder);
            var temp = $"{dataPath}.{Guid.NewGuid():N}.tmp";
            File.WriteAllBytes(temp, bytes);
            File.Move(temp, dataPath, overwrite: true);
            Save(entryPath, entry);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Picture '{entry.Url}' could not be kept on disk: {ex.Message}");
            return;
        }
        Prune();
    }

    /// <summary>Writes what is known of a copy, and marks it as used now, by the cache's own clock.</summary>
    private void Save(string entryPath, Entry entry)
    {
        try
        {
            JsonFile.Write(entryPath, entry);
            Touch(entryPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Picture '{entry.Url}': what is known of its copy could not be written: {ex.Message}");
        }
    }

    /// <summary>Marks a copy as used now: the size limit removes the copies used longest ago first.</summary>
    private void Touch(string entryPath)
    {
        try
        {
            File.SetLastWriteTimeUtc(entryPath, now().UtcDateTime);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Only the order of removal suffers.
        }
    }

    /// <summary>
    /// Removes every copy (Settings ▸ Manager data ▸ Clear picture cache): the pictures are downloaded again when the
    /// openmods page shows them.
    /// </summary>
    /// <returns>How many pictures, and how many bytes of them, were removed.</returns>
    /// <exception cref="IOException">A file could not be removed, as when another program holds it.</exception>
    /// <exception cref="UnauthorizedAccessException">The folder or a file is protected.</exception>
    public static (int Pictures, long Bytes) Clear(string folder)
    {
        if (!Directory.Exists(folder))
            return (0, 0);
        var copies = new DirectoryInfo(folder).GetFiles("*.bin");
        var cleared = (copies.Length, copies.Sum(copy => copy.Length));
        foreach (var file in new DirectoryInfo(folder).GetFiles())
            file.Delete();
        Log.Info($"Cleared the picture cache: {cleared.Item1} picture(s), {cleared.Item2} bytes.");
        return cleared;
    }

    /// <summary>Removes the copies used longest ago until the folder is within its size limit.</summary>
    internal void Prune()
    {
        lock (pruning)
        {
            try
            {
                var copies = new DirectoryInfo(folder).EnumerateFiles("*.bin")
                    .Select(data => (Data: data, Entry: new FileInfo(Path.ChangeExtension(data.FullName, ".json"))))
                    .Select(copy => (copy.Data, copy.Entry, Used: copy.Entry.Exists ? copy.Entry.LastWriteTimeUtc : copy.Data.LastWriteTimeUtc))
                    .OrderBy(copy => copy.Used)
                    .ToList();
                var total = copies.Sum(copy => copy.Data.Length);
                foreach (var copy in copies)
                {
                    if (total <= maxBytes)
                        break;
                    total -= copy.Data.Length;
                    FileSystem.TryDeleteFile(copy.Data.FullName);
                    FileSystem.TryDeleteFile(copy.Entry.FullName);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Warn($"The picture cache could not be made smaller: {ex.Message}");
            }
        }
    }

    /// <summary>The picture's file name in the cache: the SHA-256 of its address.</summary>
    private static string Key(Uri uri) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(uri.AbsoluteUri))).ToLowerInvariant();

    /// <summary>The body, or null when it is larger than <see cref="MaxPictureBytes"/>, also without a length in the header.</summary>
    private static async Task<byte[]?> ReadAtMostAsync(HttpContent content, CancellationToken cancellation)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellation).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellation).ConfigureAwait(false)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length > MaxPictureBytes)
                return null;
        }
        return buffer.ToArray();
    }
}
