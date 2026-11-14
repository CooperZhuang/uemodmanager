namespace UEManager.Core.Mods;

/// <summary>Short type names for the interface: PAK, IoStore, Blueprint, UE4SS Lua, UE4SS DLL, UE4SS files, PBZModLoader, game files, exe files.</summary>
public static class ModTypes
{
    /// <summary>A folder for UE4SS's Mods folder without code (<see cref="PackageKind.Ue4ssFolder"/>), on its own.</summary>
    public const string Ue4ssFiles = "UE4SS files";

    /// <summary>A file the download places in one of the game's own folders, such as a keybinding preset.</summary>
    public const string GameFiles = "Game files";

    /// <summary>Files that go next to the game's exe, such as a proxy DLL and its add-on.</summary>
    public const string ExeFiles = "Exe files";

    /// <summary>
    /// A download on the openmods page with none of the types above that its paths alone show, such as a tool's exe or a
    /// .usmap file; see <see cref="Catalog.CatalogTypes"/>.
    /// </summary>
    public const string Other = "Other";

    /// <summary>
    /// A script mod for Phantom Blade Zero's PBZModLoader, in the loader's mods folder (see
    /// <see cref="PackageScanner.IsModLoaderModPath"/>). Its files sit in the game's folders, but it runs only in the
    /// loader, which this name tells, where "Game files" did not.
    /// </summary>
    public const string ModLoader = PackageScanner.ForModLoader;

    /// <param name="files">Each file's deploy target and path below that target.</param>
    public static string Label(IEnumerable<(DeployTarget Target, string Path)> files)
    {
        var list = files.ToList();
        // A download with a PBZModLoader mod runs only with the loader, whatever else it brings.
        if (list.Any(f => f.Target == DeployTarget.InstallRoot && PackageScanner.IsModLoaderModPath(f.Path)))
            return ModLoader;
        if (list.Count > 0 && list.All(f => f.Target == DeployTarget.InstallRoot))
            return GameFiles;
        if (list.Count > 0 && list.All(f => f.Target == DeployTarget.ExeFolder))
            return ExeFiles;
        var ue4ss = list.Where(f => f.Target == DeployTarget.Ue4ssMods).Select(f => "/" + f.Path.Replace('\\', '/')).ToList();
        var lua = ue4ss.Any(p => p.Contains("/Scripts/", StringComparison.OrdinalIgnoreCase));
        var native = HasUe4ssDll(list);
        if (lua || native)
            return (lua, native) switch
            {
                (true, true) => "UE4SS Lua + DLL",
                (false, true) => "UE4SS DLL",
                _ => "UE4SS Lua",
            };
        // A UE4SS folder without code, such as the one a Blueprint mod writes its settings to, says nothing of the type.
        if (list.Any(f => f.Target == DeployTarget.LogicMods))
            return "Blueprint";
        if (list.All(f => f.Target == DeployTarget.Ue4ssMods) && list.Count > 0)
            return Ue4ssFiles;
        return list.Any(f => FileSystem.HasExtension(f.Path, FileSystem.IoStoreExtensions)) ? "IoStore" : "PAK";
    }

    /// <summary>
    /// A UE4SS C++ mod is among the files: a DLL in a dlls folder of UE4SS's Mods folder. Such a mod is built against
    /// one UE4SS build, and may not load with another one.
    /// </summary>
    public static bool HasUe4ssDll(IEnumerable<(DeployTarget Target, string Path)> files) =>
        files.Any(f => f.Target == DeployTarget.Ue4ssMods
            && ("/" + f.Path.Replace('\\', '/')).Contains("/dlls/", StringComparison.OrdinalIgnoreCase));
}
