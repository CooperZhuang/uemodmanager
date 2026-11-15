using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using UEManager.Core.Mods;

namespace UEManager.Core.Catalog;

/// <summary>A release of a mod on GitHub, with the text its author wrote for it.</summary>
/// <param name="Tag">The release's tag, such as "v1.2".</param>
/// <param name="Name">The release's title; null when it has none.</param>
/// <param name="Body">The release text, in GitHub's Markdown; empty when the author wrote none.</param>
/// <param name="Url">The release's page on GitHub.</param>
public sealed record ReleaseNote(string Tag, string? Name, DateTimeOffset? Date, string Body, string? Url, bool IsPreRelease)
{
    /// <summary>The version without the tag's "v": "1.2" for "v1.2".</summary>
    public string Version => ModVersion.FromTag(Tag);
}

public enum NoteBlockKind
{
    Heading,
    Bullet,
    Paragraph,
    Code,
}

/// <summary>A part of a release text: a heading, a list item, a paragraph or a block of code, as plain text.</summary>
/// <param name="Level">A heading's level (1 to 6), or how deep a list item is (0 at the top).</param>
/// <param name="Marker">A list item's mark: "•", or the number of a numbered list, such as "2.".</param>
public sealed record NoteBlock(NoteBlockKind Kind, string Text, int Level = 0, string Marker = "•");

/// <summary>
/// The changelog of an openmods mod: the texts of its newest releases on GitHub, read on a click only. GitHub answers
/// 60 such questions an hour from one PC without a login.
/// </summary>
public static partial class ModReleases
{
    /// <summary>How many releases are read: the newest ones; the rest are on GitHub.</summary>
    public const int Count = 10;

    /// <summary>"owner/repo" of a GitHub source; null for any other source.</summary>
    public static string? RepoOf(string? source)
    {
        if (!Uri.TryCreate(source, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps
            || !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
            return null;
        var id = OpenMods.IdFromSource(source!);
        return RepoName().IsMatch(id) ? id : null;
    }

    /// <summary>The releases page of a repository, for "All releases on GitHub".</summary>
    public static string ReleasesPage(string repo) => $"https://github.com/{repo}/releases";

    /// <summary>The newest releases of a repository, newest first; drafts are not shown by GitHub.</summary>
    /// <exception cref="ModException">GitHub could not be reached, refused, or answered something else.</exception>
    public static async Task<IReadOnlyList<ReleaseNote>> FetchAsync(HttpClient http, string repo, CancellationToken cancellation = default)
    {
        var url = $"https://api.github.com/repos/{repo}/releases?per_page={Count}";
        string json;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("Accept", "application/vnd.github+json");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            using var response = await http.SendAsync(request, timeout.Token).ConfigureAwait(false);
            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests
                && response.Headers.TryGetValues("X-RateLimit-Remaining", out var remaining) && remaining.FirstOrDefault() == "0")
                throw new ModException($"GitHub answers only a few questions an hour from one PC. Try again later.");
            if (response.StatusCode == HttpStatusCode.NotFound)
                throw new ModException($"GitHub has no releases page for this mod.");
            if (!response.IsSuccessStatusCode)
                throw new ModException($"The changelog could not be loaded: GitHub answered {(int)response.StatusCode}.");
            json = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            Log.Warn($"The releases of {repo} could not be read: {ex.Message}");
            throw new ModException($"GitHub could not be reached: {ex.Message}");
        }
        try
        {
            return Parse(json);
        }
        catch (JsonException ex)
        {
            Log.Warn($"The releases of {repo} could not be read: {ex.Message}");
            throw new ModException($"GitHub's answer could not be read.");
        }
    }

    /// <summary>GitHub's list of releases; entries without a tag are left out.</summary>
    /// <exception cref="JsonException">The text is not such a list.</exception>
    internal static IReadOnlyList<ReleaseNote> Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            throw new JsonException("Not a list of releases.");
        var notes = new List<ReleaseNote>();
        foreach (var release in document.RootElement.EnumerateArray())
        {
            if (release.ValueKind != JsonValueKind.Object || Text(release, "tag_name") is not { } tag
                || (release.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True))
                continue;
            DateTimeOffset? date = Text(release, "published_at") is { } published && DateTimeOffset.TryParse(published, out var parsed) ? parsed : null;
            var url = Text(release, "html_url") is { } page && page.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase) ? page : null;
            notes.Add(new ReleaseNote(tag, Text(release, "name"), date, Text(release, "body") ?? "", url,
                release.TryGetProperty("prerelease", out var pre) && pre.ValueKind == JsonValueKind.True));
        }
        return notes;
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!.Trim()
            : null;

    /// <summary>
    /// A release text in GitHub's Markdown as plain blocks: headings, list items, paragraphs and code. Inline marks
    /// (bold, links, code) keep only their text; pictures and HTML tags are left out, so nothing is loaded from a text.
    /// </summary>
    public static IReadOnlyList<NoteBlock> Blocks(string markdown)
    {
        var blocks = new List<NoteBlock>();
        var paragraph = new StringBuilder();
        var code = new StringBuilder();
        var inCode = false;

        void EndParagraph()
        {
            var text = Inline(paragraph.ToString());
            if (text.Length > 0)
                blocks.Add(new NoteBlock(NoteBlockKind.Paragraph, text));
            paragraph.Clear();
        }

        foreach (var raw in markdown.Replace("\r", "").Split('\n'))
        {
            if (raw.TrimStart().StartsWith("```", StringComparison.Ordinal) || raw.TrimStart().StartsWith("~~~", StringComparison.Ordinal))
            {
                if (inCode)
                {
                    blocks.Add(new NoteBlock(NoteBlockKind.Code, code.ToString().TrimEnd('\n')));
                    code.Clear();
                }
                else
                {
                    EndParagraph();
                }
                inCode = !inCode;
                continue;
            }
            if (inCode)
            {
                code.Append(raw).Append('\n');
                continue;
            }

            var line = raw.TrimEnd();
            var trimmed = line.TrimStart();
            if (trimmed.Length == 0 || Rule().IsMatch(trimmed))
            {
                EndParagraph();
                continue;
            }
            if (Heading().Match(trimmed) is { Success: true } heading)
            {
                EndParagraph();
                if (Inline(heading.Groups[2].Value) is { Length: > 0 } title)
                    blocks.Add(new NoteBlock(NoteBlockKind.Heading, title, heading.Groups[1].Length));
                continue;
            }
            if (ListItem().Match(line) is { Success: true } item)
            {
                EndParagraph();
                var marker = item.Groups[2].Value is var mark && char.IsDigit(mark[0]) ? mark : "•";
                if (Inline(item.Groups[3].Value) is { Length: > 0 } text)
                    blocks.Add(new NoteBlock(NoteBlockKind.Bullet, text, Math.Min(item.Groups[1].Value.Replace("\t", "  ").Length / 2, 3), marker));
                continue;
            }
            // A line that goes on the list item above it, as Markdown allows.
            if (paragraph.Length == 0 && blocks.Count > 0 && blocks[^1].Kind == NoteBlockKind.Bullet && line.StartsWith("  ", StringComparison.Ordinal))
            {
                blocks[^1] = blocks[^1] with { Text = $"{blocks[^1].Text} {Inline(trimmed)}".Trim() };
                continue;
            }
            paragraph.Append(trimmed.TrimStart('>').TrimStart()).Append(' ');
        }
        if (inCode && code.Length > 0)
            blocks.Add(new NoteBlock(NoteBlockKind.Code, code.ToString().TrimEnd('\n')));
        EndParagraph();
        return blocks;
    }

    /// <summary>A line's text without Markdown's inline marks and without HTML tags.</summary>
    internal static string Inline(string text)
    {
        // An escaped mark, such as \*, is text: it is set aside first, so no other rule reads it as a mark.
        text = Escape().Replace(text, match => ((char)(EscapedBase + match.Groups[1].Value[0])).ToString());
        text = Picture().Replace(text, "");
        text = Link().Replace(text, "$1");
        text = AutoLink().Replace(text, "$1");
        // A space, so the words on both sides of a tag stay apart.
        text = Tag().Replace(text, " ");
        text = Bold().Replace(text, "$2");
        text = Italic().Replace(text, "$1");
        text = CodeSpan().Replace(text, "$1");
        text = Escaped().Replace(text, match => ((char)(match.Value[0] - EscapedBase)).ToString());
        text = WebUtility.HtmlDecode(text);
        return Spaces().Replace(text, " ").Trim();
    }

    /// <summary>Where an escaped mark waits, in the private use area of Unicode, until the other rules are done.</summary>
    private const int EscapedBase = 0xE000;

    [GeneratedRegex("[-]")]
    private static partial Regex Escaped();

    [GeneratedRegex(@"^[A-Za-z0-9-]+/[A-Za-z0-9._-]+$")]
    private static partial Regex RepoName();

    [GeneratedRegex(@"^(#{1,6})\s+(.*?)\s*#*$")]
    private static partial Regex Heading();

    [GeneratedRegex(@"^(\s*)([-*+]|\d{1,3}[.)])\s+(.*)$")]
    private static partial Regex ListItem();

    [GeneratedRegex(@"^([-*_])(\s*\1){2,}$")]
    private static partial Regex Rule();

    [GeneratedRegex(@"!\[[^\]]*\]\([^)]*\)")]
    private static partial Regex Picture();

    [GeneratedRegex(@"\[([^\]]*)\]\([^)]*\)")]
    private static partial Regex Link();

    [GeneratedRegex(@"<(https?://[^>\s]+)>")]
    private static partial Regex AutoLink();

    [GeneratedRegex(@"</?[A-Za-z][^>]*>")]
    private static partial Regex Tag();

    [GeneratedRegex(@"(\*\*|__)(.+?)\1")]
    private static partial Regex Bold();

    [GeneratedRegex(@"(?<![\w*])\*(\S(?:.*?\S)?)\*(?![\w*])")]
    private static partial Regex Italic();

    [GeneratedRegex(@"`([^`]*)`")]
    private static partial Regex CodeSpan();

    [GeneratedRegex(@"\\([\\`*_{}\[\]()#+\-.!|>])")]
    private static partial Regex Escape();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
