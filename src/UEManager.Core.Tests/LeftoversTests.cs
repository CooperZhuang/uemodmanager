using UEManager.Core.Games;
using UEManager.Core.Mods;
using UEManager.Core.Storage;

namespace UEManager.Core.Tests;

/// <summary>
/// The files a mod leaves in the game after an uninstall, such as logs it wrote while the game ran: the uninstall
/// reports them, keeps them, and can move them into the backups.
/// </summary>
public class LeftoversTests : IDisposable
{
    private readonly TestFolder folder = new();
    private readonly GameInstall game;
    private readonly ModManager manager;

    public LeftoversTests()
    {
        game = GameLocator.Resolve(TestFolder.Profile(), folder.Game(), GameStore.Manual)!;
        manager = new ModManager(game, new AppPaths(folder.Combine("Data")), _ => false);
    }

    public void Dispose() => folder.Dispose();

    private ModRecord InstallUe4ssMod(string name)
    {
        folder.File($@"Downloads\{name}\ue4ss\Mods\{name}\enabled.txt", "");
        folder.File($@"Downloads\{name}\ue4ss\Mods\{name}\dlls\main.dll", "dll");
        using var preview = manager.Inspect(folder.Combine("Downloads", name));
        return manager.Install(preview, preview.Packages.ToList(), name, "1.0");
    }

    /// <summary>A log the mod writes while the game runs: not part of its download, so its record does not own it.</summary>
    private string WriteRuntimeLog(ModRecord mod, string name = "compatibility.log", string text = "Verified UE4SS ...")
    {
        var deployed = Path.GetDirectoryName(mod.Files.First(file => file.DeployedPath is not null).DeployedPath!)!;
        var path = Path.Combine(deployed, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    [Fact]
    public void A_log_the_mod_wrote_is_left_behind_and_reported()
    {
        var mod = InstallUe4ssMod("Bypass");
        var log = WriteRuntimeLog(mod);

        var leftovers = manager.LeftoversOf(mod.Id);

        var leftover = Assert.Single(leftovers);
        Assert.Equal(log, leftover.Path);
        Assert.True(leftover.IsLog);
        Assert.Contains("compatibility.log", leftover.RelativePath);
    }

    [Fact]
    public void Uninstalling_keeps_the_file_and_returns_it()
    {
        var mod = InstallUe4ssMod("Bypass");
        var log = WriteRuntimeLog(mod);

        var result = manager.Uninstall(mod.Id);

        Assert.True(File.Exists(log));
        Assert.Null(result.LeftoverBackup);
        Assert.Single(result.Leftovers);
        Assert.Empty(manager.Mods);
    }

    [Fact]
    public void The_files_can_be_moved_into_the_backups()
    {
        var mod = InstallUe4ssMod("Bypass");
        var log = WriteRuntimeLog(mod);
        var data = WriteRuntimeLog(mod, "dlls/state.bin", "data");

        var result = manager.Uninstall(mod.Id, moveLeftovers: true);

        Assert.NotNull(result.LeftoverBackup);
        Assert.False(File.Exists(log));
        Assert.False(File.Exists(data));
        Assert.False(Directory.Exists(Path.GetDirectoryName(log)!));
        var moved = Directory.GetFiles(result.LeftoverBackup!, "*", SearchOption.AllDirectories)
            .Select(file => Path.GetFileName(file)!)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        Assert.Equal(["compatibility.log", "state.bin"], moved);
    }

    [Fact]
    public void Files_in_a_shared_folder_are_the_players()
    {
        folder.File(@"Downloads\Sharp\Sharp_P.pak", "pak");
        using var preview = manager.Inspect(folder.Combine("Downloads", "Sharp"));
        var mod = manager.Install(preview, preview.Packages.ToList(), "Sharp", null);
        var shared = Path.Combine(game.ModsPath, "SomethingElse_P.pak");
        File.WriteAllText(shared, "not ours");

        Assert.Empty(manager.LeftoversOf(mod.Id));
    }

    [Fact]
    public void Another_mods_files_are_not_left_behind()
    {
        var first = InstallUe4ssMod("First");
        InstallUe4ssMod("Second");

        Assert.Empty(manager.LeftoversOf(first.Id));
    }

    // Only a UE4SS mod's own folder counts. Everywhere else a file the mod did not place can be the game's or the
    // player's: these cases once counted the game's exe, a save and a script loader as a mod's leftovers.

    [Fact]
    public void A_game_files_mod_leaves_no_saves_settings_or_exe_behind()
    {
        folder.File(@"Downloads\Keys\TestGame\Saved\Config\Windows\Input.ini", "[keys]");
        using (var preview = manager.Inspect(folder.Combine("Downloads", "Keys")))
            manager.Install(preview, preview.Packages.ToList(), "Keys", "1.0");
        folder.File(@"Game\TestGame\Saved\SaveGames\slot1.sav", "the player's save");
        folder.File(@"Game\TestGame\Saved\Config\Windows\GameUserSettings.ini", "the game's own settings");

        Assert.Empty(manager.LeftoversOf(manager.Mods.Single().Id));
    }

    [Fact]
    public void A_script_mod_leaves_no_loader_behind()
    {
        folder.File(@"Downloads\Markers\TestGame\Content\JavaScript\PBZModsMenu\Mods\Markers\main.js", "a script");
        folder.File(@"Game\TestGame\Content\JavaScript\PBZModsMenu\bootstrap.js", "Mods Menu, put in by hand");
        using (var preview = manager.Inspect(folder.Combine("Downloads", "Markers")))
            manager.Install(preview, preview.Packages.ToList(), "Markers", "1.0");

        Assert.Empty(manager.LeftoversOf(manager.Mods.Single().Id));
    }

    [Fact]
    public void A_folder_of_UE4SS_itself_is_never_a_mods_own()
    {
        // A mod that brings a file into UE4SS's own Blueprint loader, and one in the shared libraries.
        folder.File(@"Downloads\Patch\ue4ss\Mods\BPModLoaderMod\Scripts\patch.lua", "a patch");
        folder.File(@"Downloads\Patch\ue4ss\Mods\shared\Helper\helper.lua", "a library");
        ModRecord patch;
        using (var preview = manager.Inspect(folder.Combine("Downloads", "Patch")))
            patch = manager.Install(preview, preview.Packages.ToList(), "Patch", "1.0");
        // UE4SS's Mods folder, from where the patch went: BPModLoaderMod\Scripts\patch.lua below it.
        var placed = patch.Files.Single(file => file.FileName.EndsWith("patch.lua")).DeployedPath!;
        var mods = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(placed)!)!)!;
        Directory.CreateDirectory(Path.Combine(mods, "shared"));
        File.WriteAllText(Path.Combine(mods, "BPModLoaderMod", "Scripts", "main.lua"), "UE4SS's own, put in by hand");
        File.WriteAllText(Path.Combine(mods, "shared", "Other.lua"), "another library");

        Assert.Empty(manager.LeftoversOf(manager.Mods.Single().Id));
    }

    [Fact]
    public void A_mod_that_is_off_still_has_its_leftovers()
    {
        var mod = InstallUe4ssMod("Bypass");
        var log = WriteRuntimeLog(mod);
        manager.SetEnabled(mod.Id, false);

        Assert.Equal(log, Assert.Single(manager.LeftoversOf(mod.Id)).Path);
    }

    [Fact]
    public void Moving_takes_the_mods_folder_but_not_UE4SSs_Mods_folder()
    {
        var mod = InstallUe4ssMod("Bypass");
        WriteRuntimeLog(mod);
        // The mod's own folder: the one that holds its enabled.txt, right below UE4SS's Mods folder.
        var modFolder = Path.GetDirectoryName(mod.Files.Single(file => file.FileName.EndsWith("enabled.txt")).DeployedPath!)!;

        manager.Uninstall(mod.Id, moveLeftovers: true);

        Assert.False(Directory.Exists(modFolder));
        Assert.True(Directory.Exists(Path.GetDirectoryName(modFolder)!));
    }

    [Fact]
    public void Moving_refuses_a_file_outside_a_UE4SS_mod_folder()
    {
        var save = Path.Combine(game.RootPath, "TestGame", "Saved", "SaveGames", "slot1.sav");
        Directory.CreateDirectory(Path.GetDirectoryName(save)!);
        File.WriteAllText(save, "the player's save");

        var moved = manager.MoveLeftoversToBackups([new ModLeftover(save, Path.GetRelativePath(game.RootPath, save), 17)], "Anything");

        Assert.Null(moved);
        Assert.True(File.Exists(save));
    }
}
