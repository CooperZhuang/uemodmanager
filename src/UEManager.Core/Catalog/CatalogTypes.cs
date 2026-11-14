using UEManager.Core.Mods;

namespace UEManager.Core.Catalog;

/// <summary>
/// A mod's type on the openmods page, from the paths of its release zip that openmods lists. The app's own scanner
/// (<see cref="PackageScanner.ScanPaths"/>) reads them as an install would, with the game's folders, so the words are
/// those the Mods page shows after the install (<see cref="ModTypes"/>). The paths are only read, never used for a file.
/// </summary>
public static class CatalogTypes
{
    /// <summary>
    /// The type of what a one-click install of the zip would place: the parts the install takes without asking, all of
    /// them but a loader's other entries. <see cref="ModTypes.Other"/> when it would place nothing, such as a tool's exe
    /// or a .usmap file.
    /// </summary>
    /// <param name="entries">The zip's file paths, with / or \ between folders. Paths that end in / are folders and say nothing.</param>
    /// <param name="projectFolder">The game's Unreal project folder, such as "ProjectPBZ"; see <see cref="PackageScanner.Scan"/>.</param>
    /// <param name="exeFolder">The game's exe folder below the install folder, such as "platform/steam", once the game is found.</param>
    public static string Label(IEnumerable<string> entries, string? projectFolder = null, string? exeFolder = null)
    {
        var parts = PackageScanner.ScanPaths(entries, projectFolder: projectFolder, exeFolder: exeFolder).Packages
            .Where(part => !part.IsAlternate)
            .ToList();
        return parts.Count == 0
            ? ModTypes.Other
            : ModTypes.Label(parts.SelectMany(part => part.RelativePaths.Select(path => (part.Target, path))));
    }
}
