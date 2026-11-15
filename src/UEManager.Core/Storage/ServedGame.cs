using System.Text.Json;

namespace UEManager.Core.Storage;

/// <summary>
/// A download made for one game can hold <c>Data\serve.json</c>, such as <c>{"gameToServe": "phantom-blade-zero"}</c>.
/// At a start without a chosen game the game picker then shows that game in the middle of its highlight cards, the
/// others dimmed, all in the game's look (see <see cref="Games.GameChoice.Served"/>). The file is removed at the
/// player's first choice, and at a start that finds a game chosen already, such as when such a download is unpacked over
/// an app in use; from then on the picker is as usual.
/// </summary>
/// <remarks>
/// A file of its own, not a key in settings.json: unpacking such a download over an app in use must never replace the
/// player's settings.
/// </remarks>
public static class ServedGame
{
    /// <summary>
    /// The game id the file names; null without the file, and when it cannot be read or names no game. It only reads, and
    /// logs nothing: the app reads it before its log is open.
    /// </summary>
    public static string? Read(AppPaths paths)
    {
        try
        {
            return JsonFile.Read<ServeFile>(paths.ServeFile)?.GameToServe is { } id && !string.IsNullOrWhiteSpace(id) ? id.Trim() : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return null;
        }
    }

    public static bool Exists(AppPaths paths) => File.Exists(paths.ServeFile);

    /// <summary>Removes the file, so the picker is as usual from then on. False when it was there and could not be removed.</summary>
    public static bool Remove(AppPaths paths)
    {
        try
        {
            if (File.Exists(paths.ServeFile))
                File.Delete(paths.ServeFile);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"'{paths.ServeFile}' could not be removed: {ex.Message}");
            return false;
        }
    }

    private sealed class ServeFile
    {
        public string? GameToServe { get; set; }
    }
}
