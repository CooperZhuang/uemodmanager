using UEManager.Core.Mods;
using UEManager.UI.Services;

namespace UEManager.UI.ViewModels;

/// <summary>
/// Tags to offer while the player types them: their own, the most used first, then common ones in the app's language.
/// Only the tag being typed, after the last comma, narrows them; one already in the box is not offered again.
/// </summary>
internal static class TagHints
{
    /// <summary>How many are offered at once: one short row.</summary>
    public const int Shown = 8;

    private static readonly char[] Separators = [',', ';', '，', '；', '、'];

    /// <summary>Common tags of mods, in the app's language: Quality of life first, as players pick it most.</summary>
    public static IReadOnlyList<string> Common() =>
    [
        L.T("Quality of life"), L.T("Appearance"), L.T("Outfits"), L.T("Weapons"), L.T("Combat"), L.T("Gameplay"), L.T("Difficulty"),
        L.T("UI"), L.T("Graphics"), L.T("ReShade"), L.T("Performance"), L.T("Audio"), L.T("Animations"),
        L.T("Characters"), L.T("Maps"), L.T("Quests"), L.T("Cheats"), L.T("Fixes"), L.T("Utilities"),
    ];

    /// <summary>The player's own tags on these mods, the most used first.</summary>
    public static IReadOnlyList<string> Own(IEnumerable<ModRecord> mods) =>
        ModTags.Count(mods).OrderByDescending(entry => entry.Count).Select(entry => entry.Tag).ToList();

    /// <summary>The tags to offer for what is in the box now.</summary>
    /// <param name="own">The player's tags, the most used first.</param>
    public static IReadOnlyList<string> For(string? text, IReadOnlyList<string> own)
    {
        var (done, typing) = Split(text ?? "");
        var have = ModTags.Parse(done).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var all = own.Concat(Common()).Distinct(StringComparer.OrdinalIgnoreCase).Where(tag => !have.Contains(tag)).ToList();
        if (typing.Length == 0)
            return all.Take(Shown).ToList();
        return all.Where(tag => tag.StartsWith(typing, StringComparison.OrdinalIgnoreCase))
            .Concat(all.Where(tag => !tag.StartsWith(typing, StringComparison.OrdinalIgnoreCase) && tag.Contains(typing, StringComparison.OrdinalIgnoreCase)))
            .Take(Shown)
            .ToList();
    }

    /// <summary>Whether something is being typed after the last comma, so Tab can complete it.</summary>
    public static bool IsTyping(string? text) => Split(text ?? "").Typing.Length > 0;

    /// <summary>The box with the tag in place of the one being typed, and a comma for the next.</summary>
    public static string Apply(string? text, string tag)
    {
        var done = Split(text ?? "").Done;
        var space = done.Length > 0 && !char.IsWhiteSpace(done[^1]) && done[^1] is ',' or ';' ? " " : "";
        return $"{done}{space}{tag}, ";
    }

    /// <summary>What is before the last separator, with it, and the tag being typed after it.</summary>
    private static (string Done, string Typing) Split(string text)
    {
        var at = text.LastIndexOfAny(Separators);
        return at < 0 ? ("", text.Trim()) : (text[..(at + 1)], text[(at + 1)..].Trim());
    }
}
