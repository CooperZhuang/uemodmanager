namespace UEManager.Core.Mods;

/// <summary>The player's own words for a mod, such as "Combat" or "UI", by which the Mods page filters its list.</summary>
public static class ModTags
{
    /// <summary>What may stand between two tags: the comma and semicolon, also as Chinese text writes them.</summary>
    private static readonly char[] Separators = [',', ';', '，', '；', '、'];

    /// <summary>The tags in what the player typed, each once, whatever its case.</summary>
    public static List<string> Parse(string? text) =>
        (text ?? "").Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>The tags as the player reads and types them.</summary>
    public static string Join(IEnumerable<string>? tags) => string.Join(", ", tags ?? []);

    /// <summary>Whether the mod has the tag; every mod does for null, which stands for all mods.</summary>
    public static bool Has(ModRecord mod, string? tag) => tag is null || mod.Tags?.Contains(tag, StringComparer.OrdinalIgnoreCase) == true;

    /// <summary>Every tag of these mods by name, with how many of them have it. Tags that differ only in case are one.</summary>
    public static List<(string Tag, int Count)> Count(IEnumerable<ModRecord> mods) =>
        mods.SelectMany(mod => mod.Tags ?? [])
            .GroupBy(tag => tag, StringComparer.OrdinalIgnoreCase)
            .Select(group => (Tag: group.First(), Count: group.Count()))
            .OrderBy(entry => entry.Tag, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
}
