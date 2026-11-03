using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using UEManager.Core.Mods;

namespace UEManager.Core.Updates;

/// <summary>A release file of the app: its download link, size and, when the release list gives one, its SHA-256.</summary>
/// <param name="FallbackUrl">The same file on GitHub, tried when <paramref name="Url"/> fails: a copy of the release names it.</param>
public sealed record AppReleaseFile(string Name, string Url, long Size, string? Sha256, string? FallbackUrl = null)
{
    /// <summary>Where the file can be downloaded, in order.</summary>
    public IReadOnlyList<string> Urls => FallbackUrl is null || FallbackUrl == Url ? [Url] : [Url, FallbackUrl];
}

/// <summary>A release of the app on GitHub.</summary>
/// <param name="Version">The version without the tag's "v", such as "1.1.0".</param>
/// <param name="File">The zip of this build for this game, or null when the release has none.</param>
/// <param name="ListUrl">The address of the release list that gave it, as Cloudflare's copy or GitHub's API.</param>
public sealed record AppRelease(string Version, string? PageUrl, DateTimeOffset? Date, string? Notes, AppReleaseFile? File,
    string? ListUrl = null);

/// <summary>
/// Reads the app's newest release from GitHub's API ("releases/latest"), or from a copy of that answer next to a
/// copy of the zip, as on Cloudflare: there the zip's link is relative to the copy, and "github_download_url"
/// names the same file on GitHub. Every release holds one zip per build and game, named
/// "&lt;build&gt;_&lt;game id&gt;_&lt;version&gt;.zip"; the caller names the start of the one it needs.
/// </summary>
public static class AppReleases
{
    /// <summary>Reads GitHub's answer for one release.</summary>
    /// <param name="filePrefix">The start of the zip name of this build for this game, such as "uemm_phantom-blade-zero_".</param>
    /// <param name="allowHttp">Accept http links too; only for a test release list given on the command line.</param>
    /// <param name="listUrl">Where the answer came from: a relative download link is relative to it.</param>
    /// <exception cref="ModException">The answer names no version.</exception>
    /// <exception cref="JsonException">The text is not JSON.</exception>
    public static AppRelease Parse(string json, string filePrefix, bool allowHttp = false, Uri? listUrl = null)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var tag = Text(root, "tag_name") ?? throw new ModException($"The newest release names no version.");

        AppReleaseFile? file = null;
        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in assets.EnumerateArray())
            {
                var name = Text(asset, "name");
                var url = Link(asset, "browser_download_url", allowHttp, listUrl);
                if (name is null || url is null
                    || !name.StartsWith(filePrefix, StringComparison.OrdinalIgnoreCase)
                    || !name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    continue;
                var size = asset.TryGetProperty("size", out var s) && s.ValueKind == JsonValueKind.Number && s.TryGetInt64(out var bytes) ? bytes : 0;
                // GitHub gives "sha256:<hex>".
                var digest = Text(asset, "digest");
                var sha256 = digest is not null && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
                    && digest.Length == 71 && digest[7..].All(char.IsAsciiHexDigit)
                    ? digest[7..]
                    : null;
                file = new AppReleaseFile(name, url, size, sha256, Link(asset, "github_download_url", allowHttp));
                break;
            }
        }

        var date = Text(root, "published_at") is { } text
            && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var published)
                ? published
                : (DateTimeOffset?)null;
        return new AppRelease(ModVersion.FromTag(tag), Link(root, "html_url", allowHttp), date, Text(root, "body"), file);
    }

    /// <summary>How long one address of the release list may take before the next one is tried.</summary>
    public static readonly TimeSpan PerAddress = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Fetches the newest release from the first of <paramref name="urls"/> that answers: copies of the same release
    /// list on several hosts, so one that is blocked or down, as GitHub in some regions, does not stop the check. Each
    /// address gets <paramref name="perAddress"/>, <see cref="PerAddress"/> by default.
    /// </summary>
    /// <exception cref="ModException">No address gave a usable answer: the last one's problem.</exception>
    public static async Task<AppRelease> FetchLatestAsync(HttpClient http, IReadOnlyList<string> urls, string filePrefix,
        TimeSpan? perAddress = null, CancellationToken cancellation = default)
    {
        ModException? last = null;
        foreach (var url in urls)
        {
            try
            {
                return await FetchLatestAsync(http, url, filePrefix, cancellation, perAddress ?? PerAddress);
            }
            catch (ModException ex) when (!cancellation.IsCancellationRequested)
            {
                Log.Warn($"Release list '{url}': {ex.Message}");
                last = ex;
            }
        }
        throw last ?? new ModException($"The newest release could not be read: no address is set.");
    }

    /// <summary>Fetches the newest release from one address.</summary>
    /// <param name="timeLimit">How long the answer may take; 20 seconds when null.</param>
    /// <exception cref="ModException">The address could not be reached or gave no usable answer.</exception>
    public static async Task<AppRelease> FetchLatestAsync(HttpClient http, string url, string filePrefix, CancellationToken cancellation = default,
        TimeSpan? timeLimit = null)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            timeout.CancelAfter(timeLimit ?? TimeSpan.FromSeconds(20));
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            using var response = await http.SendAsync(request, timeout.Token);
            if (response.StatusCode == HttpStatusCode.NotFound)
                throw new ModException($"No release is published yet.");
            if (!response.IsSuccessStatusCode)
                throw new ModException($"The release list answered {(int)response.StatusCode} {response.ReasonPhrase}. Try again later.");
            var listUrl = new Uri(url);
            var release = Parse(await response.Content.ReadAsStringAsync(timeout.Token), filePrefix,
                allowHttp: listUrl.Scheme == Uri.UriSchemeHttp, listUrl) with { ListUrl = url };
            Log.Info($"Newest release from '{url}': {release.Version}; file for this build: {release.File?.Name ?? "none"}.");
            return release;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or UriFormatException)
        {
            throw new ModException($"The newest release could not be read: {ex.Message}");
        }
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!.Trim()
            : null;

    /// <summary>
    /// An https link (http too when allowed), else null: it is downloaded or opened in the browser. With
    /// <paramref name="baseUrl"/>, a relative link is relative to it, as a zip next to a copy of the release list.
    /// </summary>
    private static string? Link(JsonElement element, string name, bool allowHttp, Uri? baseUrl = null)
    {
        if (Text(element, name) is not { } text)
            return null;
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) && (baseUrl is null || !Uri.TryCreate(baseUrl, text, out uri)))
            return null;
        return uri.Scheme == Uri.UriSchemeHttps || (allowHttp && uri.Scheme == Uri.UriSchemeHttp) ? uri.AbsoluteUri : null;
    }
}
