using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using UEManager.Core.Mods;

namespace UEManager.Core.Updates;

/// <summary>The update state of a mod with a mod page, from the version on its page.</summary>
public enum ModSiteState
{
    /// <summary>Not checked, or the versions cannot be compared: the box stays empty.</summary>
    Unknown,
    UpToDate,
    UpdateAvailable,
}

/// <summary>
/// The version on the mod site's page of each installed mod with a mod page, read with one request for all of them
/// from the site's public API. It sends only the game's number and the mods' numbers, and needs no account. The app
/// only shows the result; the player downloads a newer version on the mod page.
/// </summary>
public static partial class ModSiteVersions
{
    /// <summary>The most mods one request asks for; more go in further requests, one after another.</summary>
    public const int PerRequest = 50;

    /// <summary>An answer larger than this is no answer to our question.</summary>
    private const int MaxAnswerBytes = 1024 * 1024;

    private const string Query = "query($filter: ModsFilter, $count: Int) { mods(filter: $filter, count: $count) { nodes { modId version status } } }";

    /// <summary>
    /// The page version per mod number, for the mods whose page is published. Null when a request failed: then the
    /// app knows nothing new, and says so in the log only.
    /// </summary>
    /// <param name="apiUrl">The site's API address, from the game profile.</param>
    /// <param name="gameId">The game's number on the site, from the game profile.</param>
    /// <param name="appVersion">Sent as Application-Version, with Application-Name "UE MM", as the site's rules ask.</param>
    public static async Task<IReadOnlyDictionary<string, string>?> FetchAsync(HttpClient http, string apiUrl, string gameId,
        IReadOnlyCollection<string> modIds, string appVersion, TimeSpan timeout, CancellationToken cancellation = default)
    {
        var found = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in modIds.Distinct(StringComparer.Ordinal).Chunk(PerRequest))
        {
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            limit.CancelAfter(timeout);
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, apiUrl)
                {
                    Content = new StringContent(Body(gameId, part), Encoding.UTF8, "application/json"),
                };
                request.Headers.Add("Application-Name", "UE MM");
                request.Headers.Add("Application-Version", appVersion);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, limit.Token);
                if (!response.IsSuccessStatusCode)
                {
                    Log.Warn($"The mod site's API answered {(int)response.StatusCode} to the update check.");
                    return null;
                }
                if (response.Content.Headers.ContentLength > MaxAnswerBytes)
                {
                    Log.Warn("The mod site's API sent too large an answer to the update check.");
                    return null;
                }
                var answer = await ReadAsync(response.Content, limit.Token);
                if (answer is null || !Read(answer, found))
                    return null;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException or JsonException or InvalidOperationException)
            {
                Log.Warn($"The update check could not reach the mod site's API: {ex.Message}");
                return null;
            }
        }
        Log.Info($"Update check: the mod site has the pages of {found.Count} of {modIds.Count} mod(s).");
        return found;
    }

    /// <summary>
    /// The request: one filter per mod, joined by OR, as the site's API needs the game with every mod number. Several
    /// mod numbers in one filter would all have to match.
    /// </summary>
    internal static string Body(string gameId, IReadOnlyCollection<string> modIds)
    {
        var filters = new JsonArray();
        foreach (var id in modIds)
        {
            filters.Add(new JsonObject
            {
                ["gameId"] = new JsonArray(new JsonObject { ["value"] = gameId, ["op"] = "EQUALS" }),
                ["modId"] = new JsonArray(new JsonObject { ["value"] = id, ["op"] = "EQUALS" }),
            });
        }
        return new JsonObject
        {
            ["query"] = Query,
            ["variables"] = new JsonObject
            {
                ["count"] = modIds.Count,
                ["filter"] = new JsonObject { ["op"] = "OR", ["filter"] = filters },
            },
        }.ToJsonString();
    }

    private static async Task<string?> ReadAsync(HttpContent content, CancellationToken cancellation)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellation);
        using var bytes = new MemoryStream();
        var buffer = new byte[16 * 1024];
        int count;
        while ((count = await stream.ReadAsync(buffer, cancellation)) > 0)
        {
            bytes.Write(buffer, 0, count);
            if (bytes.Length > MaxAnswerBytes)
            {
                Log.Warn("The mod site's API sent too large an answer to the update check.");
                return null;
            }
        }
        return Encoding.UTF8.GetString(bytes.GetBuffer(), 0, (int)bytes.Length);
    }

    /// <summary>Adds the page versions of an answer; false when it is an error, as the API reports one beside no data.</summary>
    internal static bool Read(string answer, Dictionary<string, string> found)
    {
        using var json = JsonDocument.Parse(answer, new JsonDocumentOptions { MaxDepth = 16 });
        var root = json.RootElement;
        if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0)
        {
            Log.Warn($"The mod site's API reported an error to the update check: {Shorten(errors.GetRawText())}");
            return false;
        }
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object
            || !data.TryGetProperty("mods", out var mods) || mods.ValueKind != JsonValueKind.Object
            || !mods.TryGetProperty("nodes", out var nodes) || nodes.ValueKind != JsonValueKind.Array)
        {
            Log.Warn("The mod site's API sent an answer the update check does not know.");
            return false;
        }
        foreach (var node in nodes.EnumerateArray())
        {
            // A hidden or removed page has no version to offer.
            if (node.ValueKind != JsonValueKind.Object
                || (node.TryGetProperty("status", out var status) && status.ValueKind == JsonValueKind.String && status.GetString() != "published"))
                continue;
            var id = node.TryGetProperty("modId", out var modId) ? modId.ValueKind switch
            {
                JsonValueKind.Number when modId.TryGetInt64(out var number) => number.ToString(CultureInfo.InvariantCulture),
                JsonValueKind.String => modId.GetString(),
                _ => null,
            } : null;
            var version = node.TryGetProperty("version", out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()?.Trim() : null;
            if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(version) && version.Length <= 64)
                found[id] = version;
        }
        return true;
    }

    /// <summary>
    /// The installed version against the page's: newer on the page is an update; the same, or older, is up to date, as
    /// nothing newer is there. A version with text after its numbers, such as "0.5.2.withHP" or "1.2-beta", compares by
    /// those numbers. With the same numbers, a second pass reads a single letter right after them in the alphabet's
    /// order, as a fix: "1.2b" after "1.2a", and "1.2a" after "1.2". Any other text tells only when it is the same, as
    /// "1.2-beta" comes before "1.2" and "withHP" is a kind, not an order. A version that does not start with a number
    /// counts only when it is the same text.
    /// </summary>
    public static ModSiteState Compare(string? installed, string? page)
    {
        if (string.IsNullOrWhiteSpace(installed) || string.IsNullOrWhiteSpace(page))
            return ModSiteState.Unknown;
        var mine = ModVersion.FromTag(installed);
        var theirs = ModVersion.FromTag(page);
        if (string.Equals(mine, theirs, StringComparison.OrdinalIgnoreCase))
            return ModSiteState.UpToDate;
        var myNumbers = LeadingNumbers().Match(mine);
        var theirNumbers = LeadingNumbers().Match(theirs);
        if (!myNumbers.Success || !theirNumbers.Success || ModVersion.CompareNumbers(myNumbers.Value, theirNumbers.Value) is not { } order)
            return ModSiteState.Unknown;
        if (order != 0)
            return order < 0 ? ModSiteState.UpdateAvailable : ModSiteState.UpToDate;
        var myRest = mine[myNumbers.Length..];
        var theirRest = theirs[theirNumbers.Length..];
        if (!IsFixLetter(myRest) || !IsFixLetter(theirRest))
            return ModSiteState.Unknown;
        return string.Compare(myRest, theirRest, StringComparison.OrdinalIgnoreCase) < 0 ? ModSiteState.UpdateAvailable : ModSiteState.UpToDate;
    }

    /// <summary>What may follow a version's numbers for the second pass: nothing, or one letter, such as the "b" of "1.2b".</summary>
    private static bool IsFixLetter(string rest) => rest.Length == 0 || (rest.Length == 1 && char.IsAsciiLetter(rest[0]));

    /// <summary>The dotted number a version starts with, such as "0.5.2" in "0.5.2.withHP".</summary>
    [GeneratedRegex(@"^[0-9]+(?:\.[0-9]+)*")]
    private static partial Regex LeadingNumbers();

    private static string Shorten(string text) => text.Length <= 300 ? text : text[..300] + "…";
}
