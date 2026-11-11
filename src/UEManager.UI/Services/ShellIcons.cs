using System.Runtime.InteropServices;
using UEManager.Core;

namespace UEManager.UI.Services;

/// <summary>
/// Tells Windows that the app's exe changed, as installers do. Explorer keeps file icons by path, so after an update
/// it may show the old version's icon for days; this makes it read the icon again.
/// </summary>
internal static class ShellIcons
{
    /// <summary>An item changed: the file at the path given.</summary>
    private const uint UpdateItem = 0x00002000;

    /// <summary>File types and their icons changed: Explorer drops the icons it keeps and reads them again.</summary>
    private const uint AssociationsChanged = 0x08000000;

    private const uint ItemIsPath = 0x0005;
    private const uint ItemIsList = 0x0000;

    /// <summary>The call returns at once; Explorer handles it on its own.</summary>
    private const uint DoNotWait = 0x2000;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern void SHChangeNotify(uint eventId, uint flags, string? item1, string? item2);

    /// <summary>Explorer shows the icon of the file at <paramref name="path"/> anew. A failure is only logged.</summary>
    public static void Refresh(string path)
    {
        try
        {
            SHChangeNotify(UpdateItem, ItemIsPath | DoNotWait, path, null);
            SHChangeNotify(AssociationsChanged, ItemIsList | DoNotWait, null, null);
            Log.Info($"Told Windows that '{path}' changed, so it shows the new icon.");
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            Log.Warn($"Windows could not be told to show the new icon: {ex.Message}");
        }
    }
}
