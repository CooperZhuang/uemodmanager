using UEManager.Core.Games;
using UEManager.Core.Mods;
using UEManager.Core.Storage;

namespace UEManager.Core.Tests;

/// <summary>"Reset modding" takes the game back to its original files.</summary>
public class ResetTests : IDisposable
{
    private readonly TestFolder folder = new();
    private readonly GameInstall game;
    private readonly AppPaths paths;
    private readonly ModManager manager;
    private bool gameRunning;

    public ResetTests()
    {
        game = GameLocator.Resolve(TestFolder.Profile(), folder.Game(), GameStore.Manual)!;
        paths = new AppPaths(folder.Combine("Data"));
        manager = new ModManager(game, paths, _ => gameRunning);
    }

    public void Dispose() => folder.Dispose();

    private string Win64 => game.BinariesPath;

    private ModRecord InstallPak(string name)
    {
        folder.File($@"Downloads\{name}\{name}_P.pak", name);
        return Install(folder.Combine("Downloads", name), name);
    }

    private ModRecord InstallLua(string name)
    {
        folder.File($@"Downloads\{name}\ue4ss\Mods\{name}\Scripts\main.lua", "print('" + name + "')");
        folder.File($@"Downloads\{name}\ue4ss\Mods\{name}\Scripts\config.lua", "speed = 1");
        return Install(folder.Combine("Downloads", name), name);
    }

    private ModRecord Install(string source, string name)
    {
        using var preview = manager.Inspect(source);
        return manager.Install(preview, preview.Packages.ToList(), name, null);
    }

    /// <summary>A modded game: UE4SS, an enabled pak and Lua mod, a disabled pak mod, and files put in by hand.</summary>
    private (ModRecord Sharp, ModRecord Witch, ModRecord Off) ModdedGame()
    {
        Directory.CreateDirectory(Path.Combine(Win64, "ue4ss", "Mods", "ByHand"));
        File.WriteAllText(Path.Combine(Win64, "ue4ss", "UE4SS.dll"), "");
        File.WriteAllText(Path.Combine(Win64, "dwmapi.dll"), "");
        var sharp = InstallPak("Sharp");
        var witch = InstallLua("Witch");
        var off = InstallPak("Off");
        manager.SetEnabled(off.Id, false);
        folder.File(Path.Combine(game.ModsPath, "Hand_P.pak"), "by hand");
        folder.File(Path.Combine(game.ModsPath, "Sub", "Deep_P.pak"), "by hand");
        folder.File(Path.Combine(game.LogicModsPath, "Blue.pak"), "by hand");
        return (sharp, witch, off);
    }

    [Fact]
    public void The_preview_counts_what_a_reset_changes()
    {
        ModdedGame();

        Assert.Equal(new ResetPreview(EnabledMods: 2, InstalledMods: 3, Ue4ssInstalled: true, OtherFiles: 3), manager.PreviewReset());
    }

    [Fact]
    public void A_reset_leaves_the_original_game_and_keeps_the_mods_in_the_manager()
    {
        var (sharp, witch, off) = ModdedGame();

        var result = manager.ResetModding(deleteMods: false);

        Assert.Equal(2, result.ModsTurnedOff);
        Assert.True(result.Ue4ssRemoved);
        Assert.Equal(3, result.FilesMovedAside);
        Assert.Equal(0, result.ModsDeleted);
        Assert.False(Directory.Exists(game.ModsPath));
        Assert.False(Directory.Exists(game.LogicModsPath));
        Assert.False(Directory.Exists(Path.Combine(Win64, "ue4ss")));
        Assert.False(File.Exists(Path.Combine(Win64, "dwmapi.dll")));
        Assert.True(Directory.Exists(game.PaksPath));

        Assert.NotNull(result.BackupFolder);
        Assert.Equal("by hand", File.ReadAllText(Path.Combine(result.BackupFolder, "~mods", "Hand_P.pak")));
        Assert.Equal("by hand", File.ReadAllText(Path.Combine(result.BackupFolder, "~mods", "Sub", "Deep_P.pak")));
        Assert.Equal("by hand", File.ReadAllText(Path.Combine(result.BackupFolder, "LogicMods", "Blue.pak")));

        var restarted = new ModManager(game, paths, _ => false);
        Assert.Equal(3, restarted.Mods.Count);
        Assert.All(restarted.Mods, mod => Assert.False(mod.Enabled));
        Assert.All(new[] { sharp, witch, off }, mod => Assert.True(Directory.Exists(manager.LibraryFolderOf(mod))));
        Assert.Equal(new ResetPreview(0, 3, false, 0), restarted.PreviewReset());
    }

    [Fact]
    public void A_reset_can_delete_the_mods_from_the_manager_and_backs_up_changed_settings()
    {
        var (sharp, witch, _) = ModdedGame();
        File.WriteAllText(Path.Combine(Win64, "ue4ss", "Mods", "Witch", "Scripts", "config.lua"), "speed = 9");

        var result = manager.ResetModding(deleteMods: true);

        Assert.Equal(3, result.ModsDeleted);
        Assert.Empty(manager.Mods);
        Assert.Empty(new ModManager(game, paths, _ => false).Mods);
        Assert.False(Directory.Exists(manager.LibraryFolderOf(sharp)));
        Assert.False(Directory.Exists(manager.LibraryFolderOf(witch)));
        var backups = Directory.GetFiles(paths.BackupDir(game.Profile.Id), "config.lua", SearchOption.AllDirectories);
        Assert.Equal("speed = 9", File.ReadAllText(Assert.Single(backups)));
    }

    [Fact]
    public void Nothing_is_reset_while_the_game_runs()
    {
        ModdedGame();
        gameRunning = true;

        Assert.Throws<ModException>(() => manager.ResetModding(deleteMods: true));

        Assert.True(File.Exists(Path.Combine(game.ModsPath, "Sharp_P.pak")));
        Assert.True(File.Exists(Path.Combine(Win64, "ue4ss", "UE4SS.dll")));
        Assert.Equal(3, manager.Mods.Count);
    }

    [Fact]
    public void A_mod_file_in_use_stops_the_reset_before_anything_changes()
    {
        ModdedGame();

        using (File.Open(Path.Combine(game.ModsPath, "Sharp_P.pak"), FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.Throws<ModException>(() => manager.ResetModding(deleteMods: false));

        Assert.True(File.Exists(Path.Combine(game.ModsPath, "Sharp_P.pak")));
        Assert.True(File.Exists(Path.Combine(Win64, "ue4ss", "Mods", "Witch", "Scripts", "main.lua")));
        Assert.True(File.Exists(Path.Combine(Win64, "ue4ss", "UE4SS.dll")));
        Assert.Equal(2, manager.Mods.Count(m => m.Enabled));
        Assert.Equal(new ResetPreview(2, 3, true, 3), manager.PreviewReset());
    }

    /// <summary>Resets with a watcher on the log, as the reset's view does, and returns the lines it got.</summary>
    private List<LogLine> WatchedReset(bool deleteMods, bool fails = false)
    {
        var lines = new List<LogLine>();
        using (Log.Watch(lines.Add))
        {
            if (fails)
                Assert.Throws<ModException>(() => manager.ResetModding(deleteMods));
            else
                manager.ResetModding(deleteMods);
        }
        return lines;
    }

    [Fact]
    public void A_watched_reset_names_every_file_and_folder_it_touches()
    {
        ModdedGame();

        var lines = WatchedReset(deleteMods: true);

        var text = string.Join("\n", lines.Select(l => l.Message));
        Assert.Contains($"Moving aside '{Path.Combine(game.ModsPath, "Sharp_P.pak")}'", text);
        Assert.Contains($"Moving aside '{Path.Combine(Win64, "ue4ss")}'", text);
        Assert.Contains($"Moving aside '{Path.Combine(Win64, "dwmapi.dll")}'", text);
        Assert.Contains($"Moving '{Path.Combine(game.ModsPath, "Sub", "Deep_P.pak")}'", text);
        Assert.Contains($"Moving '{Path.Combine(game.LogicModsPath, "Blue.pak")}'", text);
        Assert.Contains($"Removing the empty folder '{game.ModsPath}'", text);
        Assert.Contains("Deleting the manager's copy", text);
        Assert.All(lines, l => Assert.Equal("INFO", l.Level));
    }

    [Fact]
    public void Detail_lines_go_only_to_a_watcher()
    {
        ModdedGame();
        var lines = new List<LogLine>();
        using (Log.Watch(lines.Add)) { }

        manager.ResetModding(deleteMods: false);

        Assert.Empty(lines);
    }

    [Fact]
    public void A_mod_file_in_use_is_named_in_the_watched_lines()
    {
        ModdedGame();
        var pak = Path.Combine(game.ModsPath, "Sharp_P.pak");

        List<LogLine> lines;
        using (File.Open(pak, FileMode.Open, FileAccess.Read, FileShare.Read))
            lines = WatchedReset(deleteMods: false, fails: true);

        var warning = Assert.Single(lines, l => l.Level == "WARN");
        Assert.StartsWith($"'{pak}' could not be moved aside: ", warning.Message);
        Assert.Equal($"Moving aside '{pak}'", lines[lines.IndexOf(warning) - 1].Message);
    }

    [Fact]
    public void A_file_in_use_inside_the_UE4SS_folder_is_named_as_well_as_the_folder()
    {
        ModdedGame();
        var log = Path.Combine(Win64, "ue4ss", "UE4SS.log");
        File.WriteAllText(log, "");

        List<LogLine> lines;
        // As UE4SS writes its log while the game runs.
        using (new FileStream(log, FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
            lines = WatchedReset(deleteMods: false, fails: true);

        var warnings = lines.Where(l => l.Level == "WARN").Select(l => l.Message).ToList();
        Assert.Contains(warnings, w => w.StartsWith($"'{Path.Combine(Win64, "ue4ss")}' could not be moved aside: ", StringComparison.Ordinal));
        Assert.Contains(warnings, w => w.StartsWith($"In use or protected: '{log}'", StringComparison.Ordinal));
        Assert.True(File.Exists(Path.Combine(Win64, "ue4ss", "UE4SS.dll")));
    }
}
