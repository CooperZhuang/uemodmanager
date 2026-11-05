using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace UEManager.Core.Mods;

/// <summary>A UE4SS release package: its version, where to download it, and its SHA-256.</summary>
/// <param name="Url">The release file on GitHub.</param>
/// <param name="Files">
/// The official builds' files, by their place next to the game's exe ("ue4ss/UE4SS.dll"), each with the SHA-256s
/// it has in an official release; null when the manifest lists none. See <see cref="Ue4ssBuild"/>.
/// </param>
/// <param name="MirrorUrl">A copy of the same file next to the manifest, as on Cloudflare; tried before <paramref name="Url"/>.</param>
public sealed record Ue4ssRelease(string Version, string Url, string? Sha256, IReadOnlyDictionary<string, IReadOnlyList<string>>? Files = null,
    string? MirrorUrl = null)
{
    public string FileName => Path.GetFileName(new Uri(Url).AbsolutePath);

    /// <summary>Where the file can be downloaded, in order: the copy first, then GitHub.</summary>
    public IReadOnlyList<string> Urls => MirrorUrl is null || MirrorUrl == Url ? [Url] : [MirrorUrl, Url];
}

/// <summary>
/// The mod the app recommends for a game, shown at the top of the Mods page. The manifest names it, so it can come
/// and go without a new app version, such as a fix a later game patch makes needless.
/// </summary>
/// <param name="Repo">Its GitHub repo, as "owner/name": the newest release there, or its copy, is the version on offer.</param>
/// <param name="Folder">Its folder in UE4SS's Mods folder, so a copy installed by hand is recognized too.</param>
/// <param name="Texts">What it does, by language code.</param>
/// <param name="FilePrefix">The start of the zip's name in a release that holds several; empty for the first zip.</param>
/// <param name="Experimental">The card says so, next to "Recommended".</param>
/// <param name="Notes">The technical notes, by language code; paragraphs apart by an empty line. Null without notes.</param>
public sealed record RecommendedMod(string Repo, string Name, string? Folder, IReadOnlyDictionary<string, string> Texts, string FilePrefix = "", bool Experimental = false,
    IReadOnlyDictionary<string, string>? Notes = null)
{
    /// <summary>A shorter name for the mod's row once it is installed, where <see cref="Name"/> might not fit; null for the same name.</summary>
    public string? InstalledName { get; init; }

    /// <summary>The end of the zip's name, such as "-win64.zip", in a release whose zips differ there; empty for any.</summary>
    public string FileSuffix { get; init; } = "";

    /// <summary>The mod runs on UE4SS, so a click on the card installs or updates UE4SS first.</summary>
    public bool NeedsUe4ss { get; init; } = true;

    /// <summary>A second mod the card offers below this one, as an extra download.</summary>
    public RecommendedMod? Extra { get; init; }

    /// <summary>For an extra mod: the line under its row while the main mod is installed too, by language code.</summary>
    public IReadOnlyDictionary<string, string>? RetireTexts { get; init; }

    /// <summary>For an extra mod: the button with that line, which uninstalls it, by language code.</summary>
    public IReadOnlyDictionary<string, string>? RetireButtons { get; init; }

    /// <summary>A thank-you on the card; null without one.</summary>
    public RecommendedThanks? Thanks { get; init; }

    /// <summary>The repo's name without its owner: the folder of the mod's copy next to the manifest.</summary>
    public string RepoName => Repo.Split('/')[1];

    /// <summary>The text in a language, else in English, else none.</summary>
    public string? TextIn(string language) =>
        Texts.TryGetValue(language, out var text) || Texts.TryGetValue("en", out text) ? text : null;

    /// <summary>The technical notes in a language, else in English, else none.</summary>
    public string? NotesIn(string language) => In(Notes, language);

    public string? RetireTextIn(string language) => In(RetireTexts, language);

    public string? RetireButtonIn(string language) => In(RetireButtons, language);

    internal static string? In(IReadOnlyDictionary<string, string>? texts, string language) =>
        texts is not null && (texts.TryGetValue(language, out var text) || texts.TryGetValue("en", out text)) ? text : null;
}

/// <summary>A thank-you on the card: a green box that opens a short text with links, for the people a mod builds on.</summary>
/// <param name="Boxes">The box's text, by language code.</param>
/// <param name="Texts">The window's text, by language code; paragraphs apart by an empty line.</param>
/// <param name="Links">Where to find those people: https links only.</param>
public sealed record RecommendedThanks(IReadOnlyDictionary<string, string> Boxes, IReadOnlyDictionary<string, string> Texts, IReadOnlyList<string> Links)
{
    public string? BoxIn(string language) => RecommendedMod.In(Boxes, language);

    public string? TextIn(string language) => RecommendedMod.In(Texts, language);
}

/// <summary>The manifest as one address answered it: the address, and this game's entries, null when it has none.</summary>
public sealed record Ue4ssManifestAnswer(string Url, Ue4ssRelease? Release, RecommendedMod? Recommended = null);

/// <summary>
/// The metadata manifest that names the current UE4SS release per game, so a new
/// UE4SS build reaches users without a new app build:
/// <code>{ "ue4ss": { "&lt;game id&gt;": { "version": "...", "url": "https://...", "sha256": "...",
///   "mirror": "ue4ss/&lt;file&gt;.zip", "files": { "ue4ss/UE4SS.dll": ["&lt;sha256&gt;", ...], ... } } } }</code>
/// "files" and "mirror" are optional; older app versions ignore them. "mirror" is a copy of the release file,
/// relative to the manifest, so it comes from the same host; "url" stays the GitHub link, which older versions need.
/// The optional top-level "recommended" names a mod per game; see <see cref="RecommendedMod"/>:
/// <code>{ "recommended": { "&lt;game id&gt;": { "repo": "owner/name", "name": "...", "folder": "...",
///   "text": { "en": "...", "zh-CN": "..." } } } }</code>
/// The optional top-level "recommended2" has the same shape and comes first. Apps before 1.5.6 do not read it, so it
/// can name what they could not install right, and "recommended" stays as they know it.
/// </summary>
public static partial class Ue4ssManifest
{
    /// <summary>How long one address of the manifest may take before the next one is tried.</summary>
    public static readonly TimeSpan PerAddress = TimeSpan.FromSeconds(5);
    /// <summary>The entry for one game, or null when the manifest has none or it is incomplete.</summary>
    /// <param name="manifestUrl">Where the manifest came from: its "mirror" is relative to it.</param>
    /// <exception cref="JsonException">The text is not JSON.</exception>
    public static Ue4ssRelease? Parse(string json, string gameId, Uri? manifestUrl = null)
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
        return new Ue4ssRelease(version.Trim(), url!, Text(entry, "sha256"), OfficialFiles(entry), Mirror(entry, manifestUrl));
    }

    /// <summary>The mod the manifest recommends for one game, or null when it names none or the entry is incomplete.</summary>
    /// <exception cref="JsonException">The text is not JSON.</exception>
    public static RecommendedMod? ParseRecommended(string json, string gameId)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        foreach (var list in new[] { "recommended2", "recommended" })
        {
            if (document.RootElement.TryGetProperty(list, out var games) && games.ValueKind == JsonValueKind.Object
                && games.TryGetProperty(gameId, out var entry) && entry.ValueKind == JsonValueKind.Object)
                return RecommendedEntry(entry);
        }
        return null;
    }

    /// <summary>One mod on the card, or null when its entry is incomplete.</summary>
    private static RecommendedMod? RecommendedEntry(JsonElement entry)
    {
        var repo = Text(entry, "repo");
        var name = Text(entry, "name");
        if (string.IsNullOrWhiteSpace(name) || repo is null || !RepoName().IsMatch(repo) || repo.Contains(".."))
            return null;
        var experimental = entry.TryGetProperty("experimental", out var flag) && flag.ValueKind == JsonValueKind.True;
        return new RecommendedMod(repo, name.Trim(), Text(entry, "folder"), ByLanguage(entry, "text"), Text(entry, "filePrefix") ?? "", experimental, ByLanguage(entry, "notes"))
        {
            InstalledName = string.IsNullOrWhiteSpace(Text(entry, "installedName")) ? null : Text(entry, "installedName")!.Trim(),
            FileSuffix = Text(entry, "fileSuffix") ?? "",
            NeedsUe4ss = !(entry.TryGetProperty("ue4ss", out var ue4ss) && ue4ss.ValueKind == JsonValueKind.False),
            Extra = entry.TryGetProperty("extra", out var extra) && extra.ValueKind == JsonValueKind.Object ? RecommendedEntry(extra) : null,
            RetireTexts = ByLanguage(entry, "retireText"),
            RetireButtons = ByLanguage(entry, "retireButton"),
            Thanks = entry.TryGetProperty("thanks", out var thanks) && thanks.ValueKind == JsonValueKind.Object ? ThanksEntry(thanks) : null,
        };
    }

    /// <summary>A thank-you, or null without a box to show it. A link that is not https stays out, as a click opens it in the browser.</summary>
    private static RecommendedThanks? ThanksEntry(JsonElement entry)
    {
        var boxes = ByLanguage(entry, "box");
        if (boxes.Count == 0)
            return null;
        var links = new List<string>();
        if (entry.TryGetProperty("links", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            links.AddRange(list.EnumerateArray()
                .Where(l => l.ValueKind == JsonValueKind.String)
                .Select(l => l.GetString()!)
                .Where(l => Uri.TryCreate(l, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps));
        }
        return new RecommendedThanks(boxes, ByLanguage(entry, "text"), links);
    }

    /// <summary>The texts of a field by language code; anything but text is left out.</summary>
    private static Dictionary<string, string> ByLanguage(JsonElement entry, string field)
    {
        var texts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (entry.TryGetProperty(field, out var text) && text.ValueKind == JsonValueKind.Object)
        {
            foreach (var language in text.EnumerateObject().Where(l => l.Value.ValueKind == JsonValueKind.String))
                texts[language.Name] = language.Value.GetString()!;
        }
        return texts;
    }

    /// <summary>The entry's "mirror", relative to the manifest (or a full https link); null without one.</summary>
    private static string? Mirror(JsonElement entry, Uri? manifestUrl)
    {
        if (Text(entry, "mirror") is not { Length: > 0 } text)
            return null;
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) && (manifestUrl is null || !Uri.TryCreate(manifestUrl, text, out uri)))
            return null;
        // A test copy of the manifest on this PC may be served over http, as its mirror then is.
        return uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && manifestUrl?.Scheme == Uri.UriSchemeHttp)
            ? uri.AbsoluteUri
            : null;
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

    /// <summary>The manifest from one address: whether it answered with a manifest, and this game's entries in it.</summary>
    private static async Task<(bool Answered, Ue4ssRelease? Release, RecommendedMod? Recommended)> ReadAsync(HttpClient http, string url, string gameId, CancellationToken cancellation)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };
            using var response = await http.SendAsync(request, cancellation);
            if (!response.IsSuccessStatusCode)
            {
                Log.Warn($"UE4SS manifest '{url}' answered {(int)response.StatusCode}.");
                return (false, null, null);
            }
            var json = await response.Content.ReadAsStringAsync(cancellation);
            var release = Parse(json, gameId, new Uri(url));
            Log.Info(release is null
                ? $"UE4SS manifest '{url}' has no entry for '{gameId}'."
                : $"UE4SS manifest '{url}': {gameId} -> {release.Version}.");
            return (true, release, ParseRecommended(json, gameId));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            Log.Warn($"UE4SS manifest '{url}' could not be read: {ex.Message}");
            return (false, null, null);
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
            var (answered, release, recommended) = await ReadAsync(http, url, gameId, limit.Token);
            if (answered)
                return new Ue4ssManifestAnswer(url, release, recommended);
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

    /// <summary>A GitHub repo as "owner/name".</summary>
    [GeneratedRegex(@"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$")]
    private static partial Regex RepoName();
}
