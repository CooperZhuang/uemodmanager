using UEManager.Core.Games;
using UEManager.Core.Mods;
using UEManager.Core.Storage;
using UEManager.Core.Updates;

namespace UEManager.Core.Tests;

/// <summary>
/// A script loader's download holds its entry DLL next to the exe, and other entries under other names, of which only
/// one may be in the game. Its mods are loose scripts in a folder of its own.
/// </summary>
public class ModLoaderTests : IDisposable
{
    private readonly TestFolder folder = new();
    private readonly ModManager manager;
    private readonly GameInstall game;

    public ModLoaderTests()
    {
        game = GameLocator.Resolve(TestFolder.Profile(), folder.Game(), GameStore.Manual)!;
        manager = new ModManager(game, new AppPaths(folder.Combine("Data")));
    }

    public void Dispose() => folder.Dispose();

    private string Loader()
    {
        folder.File(@"Downloads\Loader\Loader-Install-Guide.txt", "by hand");
        folder.File(@"Downloads\Loader\TestGame\Binaries\Win64\version.dll", "the entry");
        folder.File(@"Downloads\Loader\OtherStore\version.dll", "the entry of another store");
        folder.File(@"Downloads\Loader\alternative-entries\hid\hid.dll", "an entry");
        folder.File(@"Downloads\Loader\alternative-entries\dxgi\dxgi.dll", "an entry");
        folder.File(@"Downloads\Loader\TestGame\PBZModLoader\LICENSES.txt", "the licenses");
        return folder.Combine("Downloads", "Loader");
    }

    [Fact]
    public void A_loaders_entries_are_one_choice_of_the_usual_name_and_the_alternates()
    {
        using var preview = manager.Inspect(Loader());

        var usual = Assert.Single(preview.Packages, p => p.Name == "version.dll");
        Assert.False(usual.IsAlternate);
        Assert.Null(usual.Variant);
        var alternates = preview.Packages.Where(p => p.IsAlternate).ToList();
        Assert.Equal(["dxgi", "hid"], alternates.Select(p => p.Variant).Order());
        Assert.All(alternates, p => Assert.False(p.IsUncertain));
        Assert.All(alternates, p => Assert.Equal(usual.VariantOf, p.VariantOf));
        Assert.Contains("Loader/TestGame/PBZModLoader/LICENSES.txt", preview.IgnoredFiles);
    }

    [Fact]
    public void An_other_folder_with_proxy_dlls_is_no_alternate_entry()
    {
        folder.File(@"Downloads\Reshade\Tools\dxgi.dll", "a loader");

        using var preview = manager.Inspect(folder.Combine("Downloads", "Reshade"));

        Assert.False(Assert.Single(preview.Packages).IsAlternate);
    }

    [Fact]
    public void The_chosen_entry_stays_through_an_update()
    {
        var source = Loader();
        using (var preview = manager.Inspect(source))
            manager.Install(preview, [Assert.Single(preview.Packages, p => p.Name == "hid")], "Loader", "1.0", catalogId: "a/loader");

        using var next = manager.Inspect(source);
        var check = manager.Check(next, [Assert.Single(next.Packages, p => p.Name == "version.dll")], "a/loader");

        Assert.Equal("hid", check.Replaces!.Variant);
    }

    /// <summary>Another mod with the same entry name, and scripts of its own.</summary>
    private ModRecord InstallMenu()
    {
        folder.File(@"Downloads\Menu\TestGame\Binaries\Win64\version.dll", "the menu's loader");
        folder.File(@"Downloads\Menu\TestGame\Content\JavaScript\Menu\bootstrap.js", "the start");
        folder.File(@"Downloads\Menu\TestGame\Content\JavaScript\Menu\manager.js", "the manager");
        using var preview = manager.Inspect(folder.Combine("Downloads", "Menu"));
        return manager.Install(preview, preview.Packages.ToList(), "Menu", null);
    }

    [Theory]
    [InlineData("a/loader")]
    [InlineData(null)]
    public void A_mod_that_only_shares_the_entry_name_is_no_older_copy_and_stays(string? catalogId)
    {
        InstallMenu();
        using var preview = manager.Inspect(Loader());

        var usual = manager.Check(preview, [Assert.Single(preview.Packages, p => p.Name == "version.dll")], catalogId);

        Assert.Null(usual.Replaces);
        Assert.False(usual.CanInstall);
        Assert.Contains("\"Menu\" uses the same files", usual.Problems[0].ToString());

        var alternate = Assert.Single(preview.Packages, p => p.Name == "hid");
        manager.Install(preview, [alternate], "Loader", "1.0", catalogId: catalogId);

        Assert.Equal(["Loader", "Menu"], manager.Mods.Select(m => m.Name).Order());
        Assert.True(File.Exists(Path.Combine(game.RootPath, "TestGame", "Content", "JavaScript", "Menu", "bootstrap.js")));
    }

    [Theory]
    [InlineData("a/loader")]
    [InlineData(null)]
    public void An_older_copy_installed_by_hand_is_replaced(string? catalogId)
    {
        var source = Loader();
        using (var preview = manager.Inspect(source))
            manager.Install(preview, [Assert.Single(preview.Packages, p => p.Name == "version.dll")], "Loader", "1.0");

        using var next = manager.Inspect(source);
        var check = manager.Check(next, [Assert.Single(next.Packages, p => p.Name == "version.dll")], catalogId);

        Assert.True(check.CanInstall);
        Assert.Equal("Loader", check.Replaces!.Name);
    }

    [Fact]
    public void A_mod_for_the_loader_is_one_part_and_leaves_its_folder_behind_as_it_found_it()
    {
        folder.File(@"Game\TestGame\Binaries\Win64\version.dll", "[PBZMOD] by hand");
        folder.File(@"Downloads\Regen\TestGame\PBZModLoader\mods\Regen\main.js", "the mod");
        folder.File(@"Downloads\Regen\TestGame\PBZModLoader\mods\Regen\data\credits.json", "its data");
        folder.File(@"Downloads\Regen\TestGame\PBZModLoader\mods\Regen\install.js", "its own script");
        folder.File(@"Downloads\Regen\TestGame\PBZModLoader\mods\Regen\README.md", "a readme");

        ModRecord mod;
        using (var preview = manager.Inspect(folder.Combine("Downloads", "Regen")))
        {
            var part = Assert.Single(preview.Packages);
            Assert.Equal("Regen", part.Name);
            Assert.Equal(3, part.RelativePaths.Count);
            mod = manager.Install(preview, [part], "Regen", null);
        }
        var mods = Path.Combine(game.RootPath, "TestGame", "PBZModLoader", "mods");
        Assert.True(File.Exists(Path.Combine(mods, "Regen", "data", "credits.json")));
        Assert.False(File.Exists(Path.Combine(mods, "Regen", "README.md")));

        manager.Uninstall(mod.Id);

        Assert.False(Directory.Exists(Path.Combine(mods, "Regen")));
        Assert.True(Directory.Exists(mods));
    }

    private string TwoLoaders()
    {
        folder.File(@"Downloads\Markers\TestGame\PBZModLoader\mods\Markers\main.js", "for the loader");
        folder.File(@"Downloads\Markers\TestGame\Content\JavaScript\PBZModsMenu\Mods\Markers\main.js", "for the menu");
        return folder.Combine("Downloads", "Markers");
    }

    [Fact]
    public void A_zip_for_two_loaders_is_one_choice_the_player_makes()
    {
        using var preview = manager.Inspect(TwoLoaders());

        Assert.Equal(["Mods Menu", "PBZModLoader"], preview.Packages.Select(p => p.Variant!).Order());
        Assert.All(preview.Packages, p => Assert.True(p.IsAlternate));
        Assert.Single(preview.Packages.Select(p => p.VariantOf).Distinct());
    }

    [Theory]
    [InlineData(@"TestGame\PBZModLoader\mods\Markers\main.js")]
    [InlineData(@"TestGame\Content\JavaScript\PBZModsMenu\Mods\Markers\main.js")]
    public void A_zip_for_one_loader_asks_nothing(string path)
    {
        folder.File($@"Downloads\One\{path}", "the mod");

        using var preview = manager.Inspect(folder.Combine("Downloads", "One"));

        var part = Assert.Single(preview.Packages);
        Assert.False(part.IsAlternate);
        Assert.Null(part.VariantOf);
    }

    [Fact]
    public void The_other_loaders_copy_takes_the_place_of_the_installed_one()
    {
        var source = TwoLoaders();
        folder.File(@"Game\TestGame\Binaries\Win64\version.dll", "[PBZMOD] by hand");
        using (var preview = manager.Inspect(source))
            manager.Install(preview, [preview.Packages.Single(p => p.Variant == "PBZModLoader")], "Markers", "1.0");
        var forLoader = Path.Combine(game.RootPath, "TestGame", "PBZModLoader", "mods", "Markers", "main.js");
        var forMenu = Path.Combine(game.RootPath, "TestGame", "Content", "JavaScript", "PBZModsMenu", "Mods", "Markers", "main.js");
        Assert.True(File.Exists(forLoader));
        File.Delete(Path.Combine(game.BinariesPath, "version.dll"));
        folder.File(@"Game\TestGame\Content\JavaScript\PBZModsMenu\bootstrap.js", "by hand");

        using var next = manager.Inspect(source);
        var other = next.Packages.Single(p => p.Variant == "Mods Menu");
        Assert.True(manager.IsInstalled(next.Packages.Single(p => p.Variant == "PBZModLoader")));
        Assert.False(manager.IsInstalled(other));
        Assert.Equal("Markers", manager.Check(next, [other]).Replaces!.Name);

        manager.Install(next, [other], "Markers", "1.0", replace: true);

        Assert.Single(manager.Mods);
        Assert.False(File.Exists(forLoader));
        Assert.True(File.Exists(forMenu));
    }

    private string ModsMenu()
    {
        folder.File(@"Downloads\RealMenu\TestGame\Binaries\Win64\hid.dll", "its entry");
        folder.File(@"Downloads\RealMenu\TestGame\Content\JavaScript\PBZModsMenu\bootstrap.js", "its start");
        folder.File(@"Downloads\RealMenu\TestGame\Content\JavaScript\PBZModsMenu\Mods\PBZModsMenu\main.js", "the menu");
        return folder.Combine("Downloads", "RealMenu");
    }

    private ModRecord InstallAll(string source, string name, string? catalogId = null)
    {
        using var preview = manager.Inspect(source);
        return manager.Install(preview, preview.Packages.Where(p => !p.IsAlternate).ToList(), name, null, catalogId: catalogId);
    }

    [Fact]
    public void Mods_Menu_and_the_loader_are_never_installed_together()
    {
        var menu = InstallAll(ModsMenu(), "Mods Menu");
        Assert.True(manager.IsModsMenuInstalled);
        Assert.False(manager.IsModLoaderInstalled);

        using (var loader = manager.Inspect(Loader()))
        {
            foreach (var part in loader.Packages)
                Assert.Contains("Uninstall Mods Menu first", Assert.Single(manager.Check(loader, [part], "KintaroModding/phantombladezero_pbzmodloader").Problems, p => p.ToString().Contains("Mods Menu and PBZModLoader")).ToString());
        }

        manager.SetEnabled(menu.Id, false);
        Assert.True(manager.IsModsMenuInstalled);
        manager.Uninstall(menu.Id);
        Assert.False(manager.IsModsMenuInstalled);

        InstallAll(Loader(), "PBZModLoader", "KintaroModding/phantombladezero_pbzmodloader");
        Assert.True(manager.IsModLoaderInstalled);

        using var next = manager.Inspect(ModsMenu());
        Assert.Contains(manager.Check(next, next.Packages.ToList()).Problems, p => p.ToString().Contains("PBZModLoader is installed"));
    }

    [Fact]
    public void A_loader_from_a_zip_counts_and_a_mod_for_it_does_not()
    {
        folder.File(@"Downloads\Regen\TestGame\PBZModLoader\mods\Regen\main.js", "a mod");
        folder.File(@"Game\TestGame\Binaries\Win64\version.dll", "[PBZMOD] by hand");
        InstallAll(folder.Combine("Downloads", "Regen"), "PBZModLoader Regen");
        File.Delete(Path.Combine(game.BinariesPath, "version.dll"));
        Assert.False(manager.IsModLoaderInstalled);

        folder.File(@"Downloads\PBZModLoader-2.0.1-win64\TestGame\Binaries\Win64\version.dll", "the entry");
        InstallAll(folder.Combine("Downloads", "PBZModLoader-2.0.1-win64"), "My name for it");
        Assert.True(manager.IsModLoaderInstalled);
    }

    [Fact]
    public void A_loader_or_a_menu_put_in_by_hand_counts_too()
    {
        folder.File(@"Game\TestGame\Binaries\Win64\version.dll", "[PBZMOD] by hand");
        Assert.True(manager.IsModLoaderInstalled);

        folder.File(@"Game\TestGame\Content\JavaScript\PBZModsMenu\bootstrap.js", "by hand");
        Assert.True(manager.IsModsMenuInstalled);
    }

    [Fact]
    public void A_mod_for_the_menu_is_not_the_menu()
    {
        folder.File(@"Downloads\AddOn\TestGame\Content\JavaScript\PBZModsMenu\Mods\AddOn\main.js", "a mod");
        using var preview = manager.Inspect(folder.Combine("Downloads", "AddOn"));

        Assert.False(ModManager.IsModsMenu(preview.Packages));
        Assert.True(ModManager.IsModsMenu(manager.Inspect(ModsMenu()).Packages));
    }

    [Fact]
    public void The_installed_loader_chooses_the_copy_of_a_zip_for_two_loaders()
    {
        var source = TwoLoaders();
        using (var neither = manager.Inspect(source))
            Assert.Equal("PBZModLoader", manager.PreferredChoice(neither)!.Variant);

        folder.File(@"Game\TestGame\Binaries\Win64\version.dll", "[PBZMOD] by hand");
        using (var loader = manager.Inspect(source))
            Assert.Equal("PBZModLoader", manager.PreferredChoice(loader)!.Variant);

        folder.File(@"Game\TestGame\Content\JavaScript\PBZModsMenu\bootstrap.js", "by hand");
        using (var both = manager.Inspect(source))
            Assert.Null(manager.PreferredChoice(both));

        File.Delete(Path.Combine(game.BinariesPath, "version.dll"));
        using var menu = manager.Inspect(source);
        Assert.Equal("Mods Menu", manager.PreferredChoice(menu)!.Variant);
    }

    private string LoaderMod()
    {
        folder.File(@"Downloads\Regen\TestGame\PBZModLoader\mods\Regen\main.js", "a mod");
        return folder.Combine("Downloads", "Regen");
    }

    private string MenuMod()
    {
        folder.File(@"Downloads\AddOn\TestGame\Content\JavaScript\PBZModsMenu\Mods\AddOn\main.js", "a mod");
        return folder.Combine("Downloads", "AddOn");
    }

    private bool CanInstall(string source)
    {
        using var preview = manager.Inspect(source);
        return manager.Check(preview, preview.Packages.ToList()).CanInstall;
    }

    [Fact]
    public void A_script_mod_waits_for_its_loader()
    {
        using (var loaderMod = manager.Inspect(LoaderMod()))
            Assert.Contains("runs in PBZModLoader", Assert.Single(manager.Check(loaderMod, loaderMod.Packages.ToList()).Problems).ToString());
        using (var menuMod = manager.Inspect(MenuMod()))
            Assert.Contains("runs in Mods Menu", Assert.Single(manager.Check(menuMod, menuMod.Packages.ToList()).Problems).ToString());

        folder.File(@"Game\TestGame\Binaries\Win64\version.dll", "[PBZMOD] by hand");
        Assert.True(CanInstall(LoaderMod()));
        Assert.False(CanInstall(MenuMod()));

        File.Delete(Path.Combine(game.BinariesPath, "version.dll"));
        folder.File(@"Game\TestGame\Content\JavaScript\PBZModsMenu\bootstrap.js", "by hand");
        Assert.False(CanInstall(LoaderMod()));
        Assert.True(CanInstall(MenuMod()));
    }

    [Theory]
    [InlineData("version.dll")]
    [InlineData("hid.dll")]
    [InlineData("winmm.dll")]
    [InlineData("dsound.dll")]
    [InlineData("dxgi.dll")]
    public void A_mod_is_not_stopped_when_the_loader_was_put_in_by_hand_as_the_dll_alone(string entry)
    {
        folder.File($@"Game\TestGame\Binaries\Win64\{entry}", "some bytes [PBZMOD] ready");

        Assert.True(manager.IsModLoaderInstalled);
        Assert.True(CanInstall(LoaderMod()));
    }

    [Theory]
    [InlineData(@"TestGame\PBZModLoader\loader.log", "[PBZMOD] ready loaded=0 failed=0")]
    [InlineData(@"TestGame\PBZModLoader\mods.ini", "[Mods]")]
    [InlineData(@"TestGame\PBZModLoader\LICENSES.txt", "the licenses")]
    [InlineData(@"TestGame\PBZModLoader\mods\keep.txt", "an other file")]
    [InlineData(@"TestGame\Binaries\Win64\version.dll", "an other tool")]
    [InlineData(@"TestGame\Binaries\Win64\hid.dll", "Mods Menu's entry knows of PBZModLoader and PBZModsMenu")]
    [InlineData(@"TestGame\Binaries\Win64\other.dll", "[PBZMOD] under a name the loader does not use")]
    public void What_the_loader_leaves_behind_and_other_dlls_are_no_loader(string path, string content)
    {
        folder.File($@"Game\{path}", content);

        Assert.False(manager.IsModLoaderInstalled);
        Assert.False(CanInstall(LoaderMod()));
    }

    [Fact]
    public void A_loader_that_was_uninstalled_stops_its_mods_again()
    {
        var loader = InstallAll(Loader(), "PBZModLoader", "KintaroModding/phantombladezero_pbzmodloader");
        folder.File(@"Game\TestGame\PBZModLoader\loader.log", "[PBZMOD] ready loaded=0 failed=0");
        Assert.True(CanInstall(LoaderMod()));

        manager.Uninstall(loader.Id);

        Assert.False(manager.IsModLoaderInstalled);
        Assert.False(CanInstall(LoaderMod()));
    }

    [Fact]
    public void A_dll_of_a_mod_in_the_list_is_no_sign_of_the_loader()
    {
        folder.File(@"Downloads\Reshade\dxgi.dll", "a loader");
        InstallAll(folder.Combine("Downloads", "Reshade"), "ReShade");

        Assert.False(CanInstall(LoaderMod()));
    }

    [Fact]
    public void Mods_Menu_itself_and_other_script_mods_need_no_loader()
    {
        Assert.True(CanInstall(ModsMenu()));

        folder.File(@"Downloads\Other\TestGame\Content\JavaScript\Other\main.js", "a mod");
        Assert.True(CanInstall(folder.Combine("Downloads", "Other")));
    }

    [Fact]
    public void A_zip_for_two_loaders_offers_only_the_copy_for_the_loader_in_the_game()
    {
        folder.File(@"Game\TestGame\Binaries\Win64\version.dll", "[PBZMOD] by hand");
        using var preview = manager.Inspect(TwoLoaders());

        Assert.True(manager.Check(preview, [preview.Packages.Single(p => p.Variant == "PBZModLoader")]).CanInstall);
        Assert.False(manager.Check(preview, [preview.Packages.Single(p => p.Variant == "Mods Menu")]).CanInstall);
    }

    /// <summary>PBZModLoader 1.0.0 as a player put it in by hand, with two mods and their settings.</summary>
    private void OldLoaderByHand(string entry = "version.dll")
    {
        File.WriteAllBytes(Path.Combine(game.BinariesPath, entry), System.Text.Encoding.Unicode.GetBytes("Content/JavaScript/PBZModLoader/bootstrap.js"));
        foreach (var file in new[]
                 {
                     @"Content\JavaScript\PBZModLoader\bootstrap.js", @"Content\JavaScript\PBZModLoader\manager.js",
                     @"Content\JavaScript\PBZModLoader\generated-mods\a\main.js", @"PBZModLoader\compiler\tsc.exe",
                     @"PBZModLoader\evidence\run1\loader.log", @"PBZModLoader\TOOLCHAIN-LICENSE.txt",
                     @"PBZModLoader\mods.ini", @"PBZModLoader\mods\Old\main.js", @"PBZModLoader\mods\Both\main.js",
                     @"PBZModLoader\mods\Both\main.ts", @"PBZModLoader\mods\Source\main.ts",
                 })
            folder.File($@"Game\TestGame\{file}", "by hand");
    }

    private string InProject(string path) => Path.Combine(game.RootPath, "TestGame", path);

    [Fact]
    public void The_old_loader_counts_as_the_loader_and_its_mods_are_not_stopped()
    {
        OldLoaderByHand();

        Assert.True(manager.IsModLoaderInstalled);
        Assert.True(CanInstall(LoaderMod()));
    }

    [Fact]
    public void The_old_loaders_files_go_to_a_backup_and_its_mods_and_settings_stay()
    {
        OldLoaderByHand();

        var old = manager.FindOldModLoader()!;

        Assert.Equal(5, old.Places.Count);
        Assert.Equal(["Source"], old.SourceOnlyMods);

        var backup = manager.RemoveOldModLoader(old);

        Assert.False(File.Exists(Path.Combine(game.BinariesPath, "version.dll")));
        Assert.False(Directory.Exists(InProject(@"Content\JavaScript\PBZModLoader")));
        Assert.True(Directory.Exists(InProject(@"Content\JavaScript")));
        Assert.False(Directory.Exists(InProject(@"PBZModLoader\compiler")));
        Assert.False(Directory.Exists(InProject(@"PBZModLoader\evidence")));
        Assert.False(File.Exists(InProject(@"PBZModLoader\TOOLCHAIN-LICENSE.txt")));
        Assert.True(File.Exists(InProject(@"PBZModLoader\mods.ini")));
        Assert.True(File.Exists(InProject(@"PBZModLoader\mods\Old\main.js")));
        Assert.True(File.Exists(InProject(@"PBZModLoader\mods\Source\main.ts")));
        Assert.Equal(7, Directory.EnumerateFiles(backup, "*", SearchOption.AllDirectories).Count());
        Assert.True(File.Exists(Path.Combine(backup, "TestGame", "PBZModLoader", "compiler", "tsc.exe")));
        Assert.Null(manager.FindOldModLoader());
        Assert.False(manager.IsModLoaderInstalled);
    }

    [Fact]
    public void A_file_in_use_leaves_the_old_loader_as_it_was()
    {
        OldLoaderByHand();
        var old = manager.FindOldModLoader()!;

        using (new FileStream(InProject(@"PBZModLoader\evidence\run1\loader.log"), FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.Throws<ModException>(() => manager.RemoveOldModLoader(old));

        Assert.True(File.Exists(Path.Combine(game.BinariesPath, "version.dll")));
        Assert.True(File.Exists(InProject(@"Content\JavaScript\PBZModLoader\generated-mods\a\main.js")));
        Assert.True(File.Exists(InProject(@"PBZModLoader\compiler\tsc.exe")));
        Assert.Equal(5, manager.FindOldModLoader()!.Places.Count);
    }

    [Fact]
    public void The_old_loader_on_another_entry_goes_too_and_another_tools_dll_stays()
    {
        OldLoaderByHand("hid.dll");
        folder.File(@"Game\TestGame\Binaries\Win64\version.dll", "another tool");

        var old = manager.FindOldModLoader()!;

        Assert.Contains(old.Places, p => p.EndsWith("hid.dll"));
        Assert.DoesNotContain(old.Places, p => p.EndsWith("version.dll"));

        manager.RemoveOldModLoader(old);

        Assert.True(File.Exists(Path.Combine(game.BinariesPath, "version.dll")));
    }

    [Fact]
    public void A_new_loader_put_in_by_hand_gives_way_too()
    {
        folder.File(@"Game\TestGame\Binaries\Win64\hid.dll", "[PBZMOD] by hand");

        var old = manager.FindOldModLoader()!;

        Assert.Single(old.Places);
        Assert.Empty(old.SourceOnlyMods);
    }

    [Theory]
    [InlineData(@"TestGame\Binaries\Win64\version.dll", "another tool")]
    [InlineData(@"TestGame\Binaries\Win64\hid.dll", "Mods Menu's entry knows of PBZModLoader and PBZModsMenu")]
    [InlineData(@"TestGame\PBZModLoader\mods.ini", "[Mods]")]
    [InlineData(@"TestGame\PBZModLoader\loader.log", "[PBZMOD] ready")]
    public void Nothing_else_is_taken_for_an_old_loader(string path, string content)
    {
        folder.File($@"Game\{path}", content);

        Assert.Null(manager.FindOldModLoader());
    }

    [Fact]
    public void The_managers_own_loader_is_no_old_loader()
    {
        folder.File(@"Downloads\Own\TestGame\Binaries\Win64\version.dll", "[PBZMOD] the entry");
        InstallAll(folder.Combine("Downloads", "Own"), "PBZModLoader", "KintaroModding/phantombladezero_pbzmodloader");

        Assert.Null(manager.FindOldModLoader());
    }

    [Fact]
    public void Old_files_in_the_mod_list_are_not_moved_behind_its_back()
    {
        folder.File(@"Downloads\Listed\TestGame\Content\JavaScript\PBZModLoader\bootstrap.js", "through the manager");
        InstallAll(folder.Combine("Downloads", "Listed"), "PBZModLoader 1.0.0");

        Assert.Null(manager.FindOldModLoader());
    }

    [Fact]
    public void A_loader_on_an_other_entry_is_no_older_copy_of_the_menu_it_stays_beside()
    {
        InstallMenu();
        using var preview = manager.Inspect(Loader());

        Assert.Null(manager.Check(preview, [Assert.Single(preview.Packages, p => p.Name == "hid")]).Replaces);
    }

    [Fact]
    public void A_release_with_two_zips_gives_the_one_with_the_wanted_end()
    {
        const string json = """
            { "tag_name": "v2.0.0", "html_url": "https://github.com/a/b", "published_at": "2026-11-05T17:00:59Z", "body": "",
              "assets": [
                { "name": "Loader-2.0.0-developer.zip", "size": 5, "browser_download_url": "https://github.com/a/developer.zip" },
                { "name": "Loader-2.0.0-win64.zip", "size": 3, "browser_download_url": "https://github.com/a/win64.zip" },
                { "name": "Loader-2.0.0-win64.zip.manifest.json", "size": 1, "browser_download_url": "https://github.com/a/manifest.json" } ] }
            """;

        Assert.Equal("Loader-2.0.0-win64.zip", AppReleases.Parse(json, "", fileSuffix: "-win64.zip").File!.Name);
        Assert.Equal("Loader-2.0.0-developer.zip", AppReleases.Parse(json, "").File!.Name);
        Assert.Null(AppReleases.Parse(json, "", fileSuffix: "-linux.zip").File);
    }

    [Fact]
    public void The_new_card_entry_comes_first_and_holds_an_extra_mod()
    {
        const string json = """
            { "recommended": { "game": { "repo": "a/old", "name": "Old" }, "other": { "repo": "a/other", "name": "Other" } },
              "recommended2": { "game": { "repo": "a/loader", "name": "Loader (with a long name)", "installedName": " Loader ", "fileSuffix": "-win64.zip", "ue4ss": false,
                "extra": { "repo": "a/old", "name": "Old", "folder": "old", "experimental": true,
                  "retireText": { "en": "Not needed.", "zh-CN": "不需要。" }, "retireButton": { "en": "Uninstall Old" } } } } }
            """;

        var mod = Ue4ssManifest.ParseRecommended(json, "game")!;

        Assert.Equal("a/loader", mod.Repo);
        Assert.Equal("Loader", mod.InstalledName);
        Assert.Null(mod.Extra!.InstalledName);
        Assert.Equal("-win64.zip", mod.FileSuffix);
        Assert.False(mod.NeedsUe4ss);
        Assert.Equal("a/old", mod.Extra!.Repo);
        Assert.True(mod.Extra.NeedsUe4ss);
        Assert.Equal("不需要。", mod.Extra.RetireTextIn("zh-CN"));
        Assert.Equal("Uninstall Old", mod.Extra.RetireButtonIn("vi"));
        Assert.Null(mod.RetireTextIn("en"));
        Assert.Equal("a/other", Ue4ssManifest.ParseRecommended(json, "other")!.Repo);
    }

    [Fact]
    public void A_thank_you_keeps_https_links_only_and_needs_a_box()
    {
        const string json = """
            { "recommended": {
                "game": { "repo": "a/b", "name": "B", "thanks": { "box": { "en": "THANK YOU", "vi": "CẢM ƠN" }, "text": { "en": "Thanks." },
                  "links": [ "https://example.com/a", "http://example.com/b", "file:///c:/windows", "not a link", 3 ] } },
                "no-box": { "repo": "a/b", "name": "B", "thanks": { "text": { "en": "Thanks." } } } } }
            """;

        var thanks = Ue4ssManifest.ParseRecommended(json, "game")!.Thanks!;

        Assert.Equal("CẢM ƠN", thanks.BoxIn("vi"));
        Assert.Equal("THANK YOU", thanks.BoxIn("zh-CN"));
        Assert.Equal("Thanks.", thanks.TextIn("vi"));
        Assert.Equal("https://example.com/a", Assert.Single(thanks.Links));
        Assert.Null(Ue4ssManifest.ParseRecommended(json, "no-box")!.Thanks);
    }

    [Fact]
    public void Two_mods_can_hold_the_mark()
    {
        folder.File(@"Downloads\A\ue4ss\Mods\A\Scripts\main.lua", "a");
        folder.File(@"Downloads\B\ue4ss\Mods\B\Scripts\main.lua", "b");
        foreach (var name in new[] { "A", "B" })
        {
            using var preview = manager.Inspect(folder.Combine("Downloads", name));
            manager.Install(preview, preview.Packages.ToList(), name, null);
        }

        manager.MarkRecommended([("x/a", "A"), ("x/b", "B")]);

        Assert.All(manager.Mods, m => Assert.True(m.Recommended));
        Assert.Equal(["x/a", "x/b"], manager.Mods.Select(m => m.CatalogId!).Order());

        manager.MarkRecommended([("x/a", "A")]);

        Assert.Equal("A", Assert.Single(manager.Mods, m => m.Recommended).Name);
    }
}
