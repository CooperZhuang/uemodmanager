using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Xml.Linq;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;

namespace UEManager.UI.Services;

/// <summary>
/// The settings editor's colours for a file, by its type: Lua for config.lua, JSON, and Name=Value lines for the
/// rest (.ini, .cfg, .txt, .toml, .yaml). The rules are built into the exe, in Theme\Syntax. Their colours come from
/// the look of the game the app runs for (Color.Syntax.* in Theme\Palette.xaml and the game's own file); the colours
/// written in Theme\Syntax are only used where the look has none.
/// </summary>
internal static class Syntax
{
    private static readonly Dictionary<string, IHighlightingDefinition> Loaded = [];

    private static readonly XNamespace Xshd = "http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008";

    /// <summary>The palette key of each colour a syntax file names: sections and Lua's words are drawn as keys.</summary>
    private static string PaletteKey(string colour) => "Color.Syntax." + colour switch
    {
        "Section" or "Keyword" => "Key",
        _ => colour,
    };

    public static IHighlightingDefinition For(string path) => Named(NameFor(path));

    /// <summary>The folder report's colours, in its view.</summary>
    public static IHighlightingDefinition Report => Named("Report");

    private static IHighlightingDefinition Named(string name)
    {
        if (!Loaded.TryGetValue(name, out var definition))
            // The look is set once, at the start: a switch of game starts the app again.
            Loaded[name] = definition = Build(name, key => Application.Current?.TryFindResource(key) as Color?);
        return definition;
    }

    private static string NameFor(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".lua" => "Lua",
        ".json" => "Json",
        _ => "Ini",
    };

    /// <summary>A syntax file's rules, with each of its colours taken from <paramref name="palette"/> where it has one.</summary>
    /// <param name="name">The syntax file in Theme\Syntax: Ini, Json, Lua or Report.</param>
    /// <param name="palette">A palette key's colour, such as Color.Syntax.Key's; null keeps the syntax file's own.</param>
    internal static IHighlightingDefinition Build(string name, Func<string, Color?> palette)
    {
        using var stream = typeof(Syntax).Assembly.GetManifestResourceStream($"UEManager.UI.Syntax.{name}.xshd")
            ?? throw new InvalidOperationException($"The colours for {name} are not built into this exe.");
        var xml = XDocument.Load(stream);
        foreach (var colour in xml.Descendants(Xshd + "Color"))
        {
            if (colour.Attribute("name")?.Value is { } role && palette(PaletteKey(role)) is { } value)
                colour.SetAttributeValue("foreground", value.ToString());
        }
        using var reader = xml.CreateReader();
        return HighlightingLoader.Load(reader, HighlightingManager.Instance);
    }
}
