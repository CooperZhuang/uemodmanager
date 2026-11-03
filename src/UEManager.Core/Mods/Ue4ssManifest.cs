using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace UEManager.Core.Mods;

/// <summary>A UE4SS release package: its version, where to download it, and its SHA-256.</summary>
/// <param name="Files">
/// The official builds' files, by their place next to the game's exe ("ue4ss/UE4SS.dll"), each with the SHA-256s
/// it has in an official release; null when the manifest lists none. See <see cref="Ue4ssBuild"/>.
/// </param>
public sealed record Ue4ssRelease(string Version, string Url, string? Sha256, IReadOnlyDictionary<string, IReadOnlyList<string>>? Files = null)
{
    public string FileName => Path.GetFileName(new Uri(Url).AbsolutePath);
}

/// <summary>The manifest as one address answered it: the address, and this game's entry, null when it has none.</summary>
public sealed record Ue4ssManifestAnswer(string Url, Ue4ssRelease? Release);

/// <summary>
/// The metadata manifest that names the current UE4SS release per game, so a new
/// UE4SS build reaches users without a new app build:
/// <code>{ "ue4ss": { "&lt;game id&gt;": { "version": "...", "url": "https://...", "sha256": "...",
///   "files": { "ue4ss/UE4SS.dll": ["&lt;sha256&gt;", ...], ... } } } }</code>
/// "files" is optional; older app versions ignore it.
/// </summary>
public static partial class Ue4ssManifest
{
    /// <summary>How long one address of the manifest may take before the next one is tried.</summary>
    public static readonly TimeSpan PerAddress = TimeSpan.FromSeconds(5);
    /// <summary>The entry for one game, or null when the manifest has none or it is incomplete.</summary>
    /// <exception cref="JsonException">The text is not JSON.</exception>
    public static Ue4ssRelease? Parse(string json, string gameId)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        if (!document.RootElement.TryGetProperty("ue4ss", out var games)
            || games.ValueKind != JsonValueKind.Object
            || !games.TryGetProperty(gameId, out var entry)
            || entry.ValueKind != JsonValueKind.Object)
            return null;
        var version = Text(entry, "version");
        var url = Text(entry, "url");
        if (string.IsNullOrWhiteSpace(version) || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            return null;
        return new Ue4ssRelease(version.Trim(), url!, Text(entry, "sha256"), OfficialFiles(entry));
    }

    /// <summary>The "files" of an entry: each place with one SHA-256 or a list of them. Anything else is left out.</summary>
    private static Dictionary<string, IReadOnlyList<string>>? OfficialFiles(JsonElement entry)
    {
        if (!entry.TryGetProperty("files", out var files) || files.ValueKind != JsonValueKind.Object)
            return null;
        var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files.EnumerateObject())
        {
            var hashes = file.Value.ValueKind switch
            {
                JsonValueKind.String => [file.Value.GetString()!],
                JsonValueKind.Array => file.Value.EnumerateArray().Where(h => h.ValueKind == JsonValueKind.String).Select(h => h.GetString()!).ToList(),
                _ => new List<string>(),
            };
            var place = file.Name.Replace('\\', '/').TrimStart('/');
            // A place outside the game's exe folder is never one of UE4SS's files.
            if (hashes.Count > 0 && !place.Split('/').Contains(".."))
                result[place] = hashes;
        }
        return result.Count > 0 ? result : null;
    }

    /// <summary>
    /// Fetches the manifest and returns this game's entry. Fresh on every call: a new
    /// UE4SS build may matter. Offline or any other failure returns null quietly.
    /// </summary>
    public static async Task<Ue4ssRelease?> FetchAsync(HttpClient http, string url, string gameId, CancellationToken cancellation = default) =>
        (await ReadAsync(http, url, gameId, cancellation)).Release;

    /// <summary>The manifest from one address: whether it answered with a manifest, and this game's entry in it.</summary>
    private static async Task<(bool Answered, Ue4ssRelease? Release)> ReadAsync(HttpClient http, string url, string gameId, CancellationToken cancellation)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };
            using var response = await http.SendAsync(request, cancellation);
            if (!response.IsSuccessStatusCode)
            {
                Log.Warn($"UE4SS manifest '{url}' answered {(int)response.StatusCode}.");
                return (false, null);
            }
            var release = Parse(await response.Content.ReadAsStringAsync(cancellation), gameId);
            Log.Info(release is null
                ? $"UE4SS manifest '{url}' has no entry for '{gameId}'."
                : $"UE4SS manifest '{url}': {gameId} -> {release.Version}.");
            return (true, release);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            Log.Warn($"UE4SS manifest '{url}' could not be read: {ex.Message}");
            return (false, null);
        }
    }

    /// <summary>
    /// Fetches the manifest from the first of <paramref name="urls"/> that answers with one: copies of the same file on
    /// several hosts, so one that is blocked or down, as GitHub in some regions, does not stop the check, and one answer
    /// counts, also without this game's entry. Each address gets <paramref name="perAddress"/>, <see cref="PerAddress"/>
    /// by default. Null when none answers.
    /// </summary>
    public static async Task<Ue4ssManifestAnswer?> FetchAsync(HttpClient http, IReadOnlyList<string> urls, string gameId,
        TimeSpan? perAddress = null, CancellationToken cancellation = default)
    {
        foreach (var url in urls)
        {
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            limit.CancelAfter(perAddress ?? PerAddress);
            var (answered, release) = await ReadAsync(http, url, gameId, limit.Token);
            if (answered)
                return new Ue4ssManifestAnswer(url, release);
            if (cancellation.IsCancellationRequested)
                return null;
        }
        return null;
    }

    /// <summary>
    /// Whether <paramref name="latest"/> should be offered as an update. An unknown installed
    /// version (UE4SS installed by hand) is offered the latest one too.
    /// </summary>
    public static bool IsUpdate(string? installedVersion, Ue4ssRelease? latest) =>
        latest is not null && !string.Equals(installedVersion?.Trim(), latest.Version, StringComparison.OrdinalIgnoreCase);

    /// <summary>The version in a release file name, such as "v3.0.1-1181-g83330635" in "mortalshell2_ue4ss_v3.0.1-1181-g83330635.zip".</summary>
    public static string? VersionFromFileName(string path)
    {
        // Only an archive extension is cut off: a folder named "ue4ss_v3.0.1-7" has dots in its version.
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
        if (FileSystem.HasExtension(name, FileSystem.ArchiveExtensions))
            name = Path.GetFileNameWithoutExtension(name);
        var match = FileVersion().Match(name);
        return match.Success ? match.Value : null;
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    [GeneratedRegex(@"v\d+(?:\.\d+)+(?:-[0-9A-Za-z.-]+)?", RegexOptions.IgnoreCase)]
    private static partial Regex FileVersion();
}
