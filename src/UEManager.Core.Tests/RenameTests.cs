using UEManager.Core.Games;
using UEManager.Core.Mods;
using UEManager.Core.Storage;

namespace UEManager.Core.Tests;

/// <summary>Renaming an installed mod changes only its name and version in the mod list.</summary>
public class RenameTests : IDisposable
{
    private readonly TestFolder folder = new();
    private readonly GameInstall game;
    private readonly ModManager manager;
    private bool gameRunning;

    public RenameTests()
    {
        game = GameLocator.Resolve(TestFolder.Profile(), folder.Game(), GameStore.Manual)!;
        manager = new ModManager(game, new AppPaths(folder.Combine("Data")), _ => gameRunning);
    }

    public void Dispose() => folder.Dispose();

    private ModRecord Install(string name)
    {
        folder.File($@"Downloads\{name}\{name}_P.pak", name);
        using var preview = manager.Inspect(folder.Combine("Downloads", name));
        return manager.Install(preview, preview.Packages.ToList(), name, null);
    }

    private string Deployed(string name) => Path.Combine(game.ModsPath, name + "_P.pak");

    [Fact]
    public void A_mod_left_out_of_the_update_check_stays_so_after_a_restart_and_an_update()
    {
        var mod = Install("Sharp");
        gameRunning = true;

        // Only the mod list changes, so it works while the game runs.
        manager.SetTracked(mod.Id, false);

        Assert.True(new ModManager(game, new AppPaths(folder.Combine("Data")), _ => false).Mods.Single().NotTracked);
        gameRunning = false;
        folder.File(@"Downloads\Sharp 2\Sharp_P.pak", "newer");
        using var preview = manager.Inspect(folder.Combine("Downloads", "Sharp 2"));
        var updated = manager.Install(preview, preview.Packages.ToList(), "Sharp", "2", replace: true);
        Assert.True(updated.NotTracked);

        manager.SetTracked(updated.Id, true);
        Assert.False(manager.Mods.Single().NotTracked);
    }

    [Fact]
    public void A_new_name_and_version_survive_a_restart_and_the_game_files_stay()
    {
        var mod = Install("Sharp");
        manager.SaveSet("Mine");

        manager.Rename(mod.Id, "  Sharp Blades ", " 1.2 ");

        var restarted = new ModManager(game, new AppPaths(folder.Combine("Data")), _ => false);
        var renamed = Assert.Single(restarted.Mods);
        Assert.Equal(("Sharp Blades", "1.2"), (renamed.Name, renamed.Version));
        Assert.True(renamed.Enabled);
        Assert.True(File.Exists(Deployed("Sharp")));
        Assert.Equal([mod.Id], Assert.Single(restarted.Sets).ModIds);
        Assert.Equal(ModIntegrity.Intact, ModManager.Integrity(renamed));
    }

    [Fact]
    public void A_blank_version_means_none_and_a_blank_name_is_refused()
    {
        var mod = Install("Sharp");
        manager.Rename(mod.Id, "Sharp", "1.0");

        manager.Rename(mod.Id, "Sharp", "  ");
        Assert.Null(manager.Mods.Single().Version);

        Assert.Throws<ModException>(() => manager.Rename(mod.Id, "   ", "2.0"));
        Assert.Equal(("Sharp", (string?)null), (manager.Mods.Single().Name, manager.Mods.Single().Version));
    }

    [Fact]
    public void A_rename_works_while_the_game_runs()
    {
        var mod = Install("Sharp");
        gameRunning = true;

        manager.Rename(mod.Id, "Sharp Blades", null);

        Assert.Equal("Sharp Blades", manager.Mods.Single().Name);
    }
}
