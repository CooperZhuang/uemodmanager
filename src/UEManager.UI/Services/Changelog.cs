using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;

namespace UEManager.UI.Services;

/// <summary>One release in the changelog: its version, its date when given, and one short line per change.</summary>
public sealed record ChangelogRelease(string Version, DateOnly? Date, IReadOnlyList<string> Changes)
{
    public string DateText => Date?.ToString(L.T("d MMM yyyy"), CultureInfo.CurrentCulture) ?? "";
}

/// <summary>
/// CHANGELOG.md, built into the exe: what changed in each release, newest first, as "## 1.4.0 (2026-11-01)" headings
/// with one "- " line per change. A language may have its own lines in Localization\changelog.&lt;code&gt;.md, with
/// the same headings; a release it does not have yet shows in English.
/// </summary>
internal static partial class Changelog
{
    private const string Resource = "UEManager.UI.Changelog.md";

    private const string TranslationPrefix = "UEManager.UI.Localization.changelog.";

    /// <summary>The English changelog, the one every release has.</summary>
    public static IReadOnlyList<ChangelogRelease> Load() =>
        Parse(Read(Resource) ?? throw new InvalidOperationException($"'{Resource}' is not built into this exe."));

    /// <summary>The changelog in a language: its own lines where it has them, else the English ones.</summary>
    public static IReadOnlyList<ChangelogRelease> Load(string language) =>
        Read(TranslationPrefix + language + ".md") is { } translated ? Translate(Load(), Parse(translated)) : Load();

    /// <summary>The languages that have changelog lines of their own, such as "zh-CN".</summary>
    internal static IReadOnlyList<string> Languages() =>
        Assembly.GetExecutingAssembly().GetManifestResourceNames()
            .Where(n => n.StartsWith(TranslationPrefix, StringComparison.Ordinal) && n.EndsWith(".md", StringComparison.Ordinal))
            .Select(n => n[TranslationPrefix.Length..^".md".Length])
            .ToList();

    /// <summary>
    /// The English releases, each with the translated lines of the same version where the translation has some. The
    /// English file decides which releases there are and their dates.
    /// </summary>
    internal static IReadOnlyList<ChangelogRelease> Translate(IReadOnlyList<ChangelogRelease> english, IReadOnlyList<ChangelogRelease> translated)
    {
        var lines = translated
            .Where(r => r.Changes.Count > 0)
            .GroupBy(r => r.Version, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Changes, StringComparer.OrdinalIgnoreCase);
        return english
            .Select(release => lines.TryGetValue(release.Version, out var changes) ? release with { Changes = changes } : release)
            .ToList();
    }

    private static string? Read(string resource)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource);
        if (stream is null)
            return null;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>The releases in the order of the text; lines outside a release, such as the introduction, are left out.</summary>
    internal static IReadOnlyList<ChangelogRelease> Parse(string text)
    {
        var releases = new List<ChangelogRelease>();
        string? version = null;
        DateOnly? date = null;
        var changes = new List<string>();
        void Close()
        {
            if (version is not null)
                releases.Add(new ChangelogRelease(version, date, changes));
        }

        foreach (var raw in text.Replace("\r", "").Split('\n'))
        {
            var line = raw.Trim();
            if (Heading().Match(line) is { Success: true } heading)
            {
                Close();
                version = heading.Groups["version"].Value;
                date = DateOnly.TryParseExact(heading.Groups["date"].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
                    ? day
                    : null;
                changes = [];
            }
            else if (version is not null && line.StartsWith("- ", StringComparison.Ordinal))
            {
                changes.Add(line[2..].Trim());
            }
        }
        Close();
        return releases;
    }

    [GeneratedRegex(@"^##\s+v?(?<version>\d[^\s(]*)\s*(?:\((?<date>[^)]*)\))?")]
    private static partial Regex Heading();
}
