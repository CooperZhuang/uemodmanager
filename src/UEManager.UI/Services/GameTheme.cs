using System.IO;
using System.Windows;

namespace UEManager.UI.Services;

/// <summary>
/// The app's look for the game it runs for: the shared palette, then the game's own colours
/// (Theme\Games\&lt;game id&gt;.xaml, when the game has that file), then the controls.
/// </summary>
/// <remarks>
/// The order matters. A style in Controls.xaml takes each colour as it is read, from the dictionary added last that
/// has the key; so the game's colours must be in place before the controls are read. The look is therefore set once,
/// at the start, before the first window: a switch of game starts the app again.
/// </remarks>
internal static class GameTheme
{
    /// <param name="gameId">The game's profile id; null for the shared look, as while the app asks for the game.</param>
    public static void Apply(ResourceDictionary resources, string? gameId)
    {
        var dictionaries = resources.MergedDictionaries;
        dictionaries.Clear();
        dictionaries.Add(Read("Theme/Palette.xaml"));
        if (gameId is not null && OfGame(gameId) is { } game)
            dictionaries.Add(game);
        dictionaries.Add(Read("Theme/Controls.xaml"));
    }

    /// <summary>The game's own colours; null when the game has none, so it shows in the shared look.</summary>
    internal static ResourceDictionary? OfGame(string gameId)
    {
        try
        {
            return Read($"Theme/Games/{gameId}.xaml");
        }
        catch (IOException)
        {
            // No file for this game in the exe.
            return null;
        }
    }

    private static ResourceDictionary Read(string path) =>
        new() { Source = new Uri($"/UEModManager;component/{path}", UriKind.Relative) };
}
