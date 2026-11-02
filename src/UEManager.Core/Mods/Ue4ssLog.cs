using System.Text.RegularExpressions;

namespace UEManager.Core.Mods;

public enum Ue4ssLogLevel
{
    Info,
    Warning,
    Error,
}

/// <summary>Who wrote a line of UE4SS.log; the UE4SS page shows one tab for each.</summary>
public enum Ue4ssLogSource
{
    /// <summary>UE4SS itself, or a mod it comes with, such as BPModLoaderMod.</summary>
    Ue4ss,

    /// <summary>A mod the player added.</summary>
    Mod,

    /// <summary>A Lua script that does not say which mod it is.</summary>
    Unnamed,
}

/// <summary>
/// A line of UE4SS.log. <paramref name="Mod"/> is the mod folder the line belongs to, when it names one. A known line
/// is one UE4SS writes at every start that does no harm (see <see cref="Ue4ssLogRules"/>); it is no error or warning.
/// </summary>
public sealed record Ue4ssLogLine(
    string Text, Ue4ssLogLevel Level, Ue4ssLogSource Source = Ue4ssLogSource.Ue4ss, string? Mod = null, bool Known = false)
{
    /// <summary>An error or a warning that is not a known line.</summary>
    public bool IsProblem => Level != Ue4ssLogLevel.Info && !Known;
}

/// <summary>
/// The end of UE4SS.log, which UE4SS writes anew at every game start. Lines that speak of an error or a
/// warning are marked, and the mod folders an error names are picked out.
/// </summary>
public sealed partial record Ue4ssLog(string Path, IReadOnlyList<Ue4ssLogLine> Lines, bool Truncated, DateTime WrittenAt)
{
    /// <summary>A UE4SS log grows long with chatty mods; the end tells what went wrong.</summary>
    public const int MaxLines = 2000;

    public int ErrorCount => Lines.Count(l => l.IsProblem && l.Level == Ue4ssLogLevel.Error);

    public int WarningCount => Lines.Count(l => l.IsProblem && l.Level == Ue4ssLogLevel.Warning);

    public int KnownCount => Lines.Count(l => l.Known);

    /// <summary>Reads the log, or returns null when there is none yet, as before the first game start with UE4SS.</summary>
    /// <param name="modFolders">The folders in UE4SS's Mods folder: a line that names one belongs to that mod.</param>
    public static Ue4ssLog? Read(string path, IReadOnlyCollection<string>? modFolders = null)
    {
        if (!File.Exists(path))
            return null;
        var lines = new Queue<string>();
        var truncated = false;
        // The game keeps the log open while it runs.
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        using (var reader = new StreamReader(stream))
        {
            while (reader.ReadLine() is { } line)
            {
                lines.Enqueue(line);
                if (lines.Count > MaxLines)
                {
                    lines.Dequeue();
                    truncated = true;
                }
            }
        }
        return new Ue4ssLog(path, Parse(lines, modFolders ?? [], Ue4ssLogRules.BuiltIn), truncated, File.GetLastWriteTime(path));
    }

    /// <summary>
    /// The lines with their level and who wrote them. A line belongs to a mod when it starts the mod ("Starting Lua
    /// mod 'X'"), points into its folder (Mods\X\), names one of <paramref name="modFolders"/>, or carries the tag a Lua
    /// mod prints before its text ("[Lua] [Witch Time] …"). A tag first printed while a mod starts is that mod's;
    /// another tag belongs to the one mod whose folder name holds it, as "Witch Time" in phantombladezero_witchtime.
    /// A line without a time stamp goes on from the line before it.
    /// </summary>
    public static IReadOnlyList<Ue4ssLogLine> Parse(IEnumerable<string> lines, IReadOnlyCollection<string> modFolders, Ue4ssLogRules rules)
    {
        var mods = new HashSet<string>(modFolders, StringComparer.OrdinalIgnoreCase);
        var tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? starting = null;
        var parsed = new List<Ue4ssLogLine>();
        foreach (var text in lines)
        {
            var level = LevelOf(text);
            if (TimeStamp().Match(text) is not { Success: true } stamp)
            {
                var before = parsed.Count > 0 ? parsed[^1] : null;
                parsed.Add(new Ue4ssLogLine(text, level, before?.Source ?? Ue4ssLogSource.Ue4ss, before?.Mod));
                continue;
            }
            var message = text[stamp.Length..];
            if (rules.IsKnown(message))
            {
                parsed.Add(new Ue4ssLogLine(text, level, Ue4ssLogSource.Ue4ss, Known: true));
                continue;
            }
            if (message.StartsWith("Event loop start", StringComparison.OrdinalIgnoreCase))
                starting = null;

            string? mod = null;
            if (ModLine().Match(message) is { Success: true } named)
            {
                mod = named.Groups["mod"].Value;
                mods.Add(mod);
                if (message.StartsWith("Starting", StringComparison.Ordinal) || message.Contains("starting mod", StringComparison.OrdinalIgnoreCase))
                    starting = mod;
            }
            // Mods\shared holds Lua libraries for all mods, no mod of its own.
            else if (ModPath().Match(message) is { Success: true } path && !path.Groups["mod"].Value.Equals("shared", StringComparison.OrdinalIgnoreCase))
                mod = path.Groups["mod"].Value;
            else
                mod = mods.FirstOrDefault(m => !rules.IsUe4ssMod(m) && Names(message, m));
            if (mod is null && LuaTag().Match(message) is { Success: true } tag)
                mod = OwnerOf(tag.Groups["tag"].Value.Trim());

            var source = mod is not null ? (rules.IsUe4ssMod(mod) ? Ue4ssLogSource.Ue4ss : Ue4ssLogSource.Mod)
                : message.StartsWith("[Lua]", StringComparison.OrdinalIgnoreCase) && !rules.IsUe4ssLine(message) ? Ue4ssLogSource.Unnamed
                : Ue4ssLogSource.Ue4ss;
            parsed.Add(new Ue4ssLogLine(text, level, source, mod));
        }
        return parsed;

        string? OwnerOf(string tag)
        {
            if (tags.TryGetValue(tag, out var owner))
                return owner;
            owner = mods.Concat(rules.Ue4ssMods).FirstOrDefault(m => m.Equals(tag, StringComparison.OrdinalIgnoreCase)) ?? starting ?? Holding(tag);
            if (owner is not null)
                tags[tag] = owner;
            return owner;
        }

        // The one mod whose folder name holds the tag's letters, or whose name the tag holds.
        string? Holding(string tag)
        {
            var letters = Letters(tag);
            if (letters.Length < 4)
                return null;
            var matches = mods.Concat(rules.Ue4ssMods)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(m => Letters(m) is { Length: >= 4 } folder && (folder.Contains(letters) || letters.Contains(folder)))
                .ToList();
            return matches.Count == 1 ? matches[0] : null;
        }
    }

    public static Ue4ssLogLevel LevelOf(string line) =>
        ErrorWords().IsMatch(line) ? Ue4ssLogLevel.Error
        : WarningWords().IsMatch(line) ? Ue4ssLogLevel.Warning
        : Ue4ssLogLevel.Info;

    /// <summary>The mod folders, out of <paramref name="modFolders"/>, that error lines name or belong to; known lines are left out.</summary>
    public IReadOnlyList<string> ModsNamedInErrors(IEnumerable<string> modFolders) => modFolders
        .Where(mod => Lines.Any(l => l.IsProblem && l.Level == Ue4ssLogLevel.Error
            && (mod.Equals(l.Mod, StringComparison.OrdinalIgnoreCase) || Names(l.Text, mod))))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Order(StringComparer.OrdinalIgnoreCase)
        .ToList();

    private static bool Names(string text, string mod) =>
        text.Contains(mod, StringComparison.OrdinalIgnoreCase)
        && Regex.IsMatch(text, $@"(?<![\w-]){Regex.Escape(mod)}(?![\w-])", RegexOptions.IgnoreCase);

    private static string Letters(string name) => new(name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    [GeneratedRegex(@"\b(error|errors|exception|fatal|failed|failure)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ErrorWords();

    [GeneratedRegex(@"\b(warn|warning|warnings)\b", RegexOptions.IgnoreCase)]
    private static partial Regex WarningWords();

    /// <summary>"[2026-11-01 19:56:30.3797355] " or "[12:40:01] " at the start of a line.</summary>
    [GeneratedRegex(@"^\[(?:\d{4}-\d{2}-\d{2} )?\d{1,2}:\d{2}:\d{2}(?:\.\d+)?\] ?")]
    private static partial Regex TimeStamp();

    /// <summary>"Starting Lua mod 'X'", "Mod 'X' has enabled.txt, starting mod." and other lines about one mod.</summary>
    [GeneratedRegex(@"^(?:Starting \S+ mod|Mod) '(?<mod>[^']+)'", RegexOptions.IgnoreCase)]
    private static partial Regex ModLine();

    /// <summary>A path into a mod's folder, such as Mods\X\Scripts\main.lua or Mods/X/load_order.txt.</summary>
    [GeneratedRegex(@"(?<![\w-])Mods[\\/](?<mod>[^\\/'"":*?<>|\s]+)[\\/]", RegexOptions.IgnoreCase)]
    private static partial Regex ModPath();

    /// <summary>The tag a Lua mod prints before its text: "[Lua] [Witch Time] 1.8 loaded."</summary>
    [GeneratedRegex(@"^\[Lua\]\s*\[(?<tag>[^\]]+)\]", RegexOptions.IgnoreCase)]
    private static partial Regex LuaTag();
}
