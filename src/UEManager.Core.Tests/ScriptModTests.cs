using UEManager.Core.Games;
using UEManager.Core.Mods;
using UEManager.Core.Storage;

namespace UEManager.Core.Tests;

/// <summary>
/// A script mod keeps loose scripts below the project's Content/JavaScript folder, often with a proxy DLL next to the
/// exe that starts them. Without the scripts, the DLL alone would be a broken mod.
/// </summary>
public class ScriptModTests : IDisposable
{
    private readonly TestFolder folder = new();
    private readonly ModManager manager;
    private readonly GameInstall game;

    public ScriptModTests()
    {
        game = GameLocator.Resolve(TestFolder.Profile(), folder.Game(), GameStore.Manual)!;
        manager = new ModManager(game, new AppPaths(folder.Combine("Data")));
    }

    public void Dispose() => folder.Dispose();

    private string Scripts(params string[] below) =>
        Path.Combine([game.RootPath, "TestGame", "Content", "JavaScript", .. below]);

    /// <summary>A loader, its scripts in two folders, and a readme, as a menu mod's download has them.</summary>
    private string Download()
    {
        folder.File(@"Downloads\Menu\Menu-README.txt", "install instructions by hand");
        folder.File(@"Downloads\Menu\TestGame\Binaries\Win64\version.dll", "the loader");
        folder.File(@"Downloads\Menu\TestGame\Content\JavaScript\Menu\bootstrap.js", "the start");
        folder.File(@"Downloads\Menu\TestGame\Content\JavaScript\Menu\Mods\Menu\main.js", "the menu");
        return folder.Combine("Downloads", "Menu");
    }

    /// <summary>A mod for that menu: one script in the menu's folder.</summary>
    private string AddOn()
    {
        folder.File(@"Downloads\AddOn\TestGame\Content\JavaScript\Menu\Mods\AddOn\main.js", "the add-on");
        return folder.Combine("Downloads", "AddOn");
    }

    private ModRecord Install(string source)
    {
        using var preview = manager.Inspect(source);
        return manager.Install(preview, preview.Packages.ToList(), preview.SuggestedName, preview.SuggestedVersion);
    }

    [Fact]
    public void A_script_folder_is_one_part_and_stays_apart_from_its_loader()
    {
        using var preview = manager.Inspect(Download());

        Assert.Equal(2, preview.Packages.Count);
        var scripts = Assert.Single(preview.Packages, p => p.Name == "Menu");
        Assert.Equal(PackageKind.File, scripts.Kind);
        Assert.Equal(DeployTarget.InstallRoot, scripts.Target);
        Assert.Equal(ModTypes.GameFiles, scripts.TypeLabel);
        Assert.Equal(["TestGame/Content/JavaScript/Menu/bootstrap.js", "TestGame/Content/JavaScript/Menu/Mods/Menu/main.js"], scripts.RelativePaths);
        Assert.Equal("TestGame/Content/JavaScript", scripts.Folder);
        var loader = Assert.Single(preview.Packages, p => p.Name == "version.dll");
        Assert.Equal(["TestGame/Binaries/Win64/version.dll"], loader.RelativePaths);
        Assert.Equal("Menu/Menu-README.txt", Assert.Single(preview.IgnoredFiles));
    }

    [Fact]
    public void A_lone_script_is_known_by_its_own_path()
    {
        using var preview = manager.Inspect(AddOn());

        var part = Assert.Single(preview.Packages);
        Assert.Equal("main.js", part.Name);
        Assert.Equal("TestGame/Content/JavaScript/Menu/Mods/AddOn", part.Folder);
    }

    [Fact]
    public void Every_folder_below_content_counts_but_paks()
    {
        folder.File(@"Downloads\Other\TestGame\Content\Movies\intro.mp4", "a movie");
        folder.File(@"Downloads\Other\TestGame\Content\Mods\Route\icon.png", "an image");
        folder.File(@"Downloads\Other\TestGame\Content\Mods\Route\deep\line.png", "an image");
        folder.File(@"Downloads\Other\TestGame\Content\Paks\~mods\notes.bin", "no pak beside it");
        folder.File(@"Downloads\Other\TestGame\Binaries\Other\helper.bin", "not in the content folder");

        using var preview = manager.Inspect(folder.Combine("Downloads", "Other"));

        Assert.Equal(["TestGame/Content/Mods/Route/deep/line.png", "TestGame/Content/Mods/Route/icon.png"],
            Assert.Single(preview.Packages, p => p.Name == "Route").RelativePaths.Order(StringComparer.Ordinal));
        Assert.Equal("intro.mp4", Assert.Single(preview.Packages, p => p.Name != "Route").Name);
        Assert.Equal(2, preview.IgnoredFileCount);
    }

    [Fact]
    public void A_mod_with_images_in_the_content_folder_is_installed_whole_and_leaves_whole()
    {
        folder.File(@"Game\TestGame\Binaries\Win64\version.dll", "[PBZMOD] by hand");
        folder.File(@"Downloads\Route\README-en.txt", "install instructions by hand");
        folder.File(@"Downloads\Route\TestGame\PBZModLoader\mods\Route\main.js", "the mod");
        folder.File(@"Downloads\Route\TestGame\Content\Mods\Route\icon.png", "an image");
        folder.File(@"Downloads\Route\TestGame\Content\Mods\Route\README.txt", "a readme");

        var mod = Install(folder.Combine("Downloads", "Route"));

        var images = Path.Combine(game.RootPath, "TestGame", "Content", "Mods");
        Assert.True(File.Exists(Path.Combine(images, "Route", "icon.png")));
        Assert.False(File.Exists(Path.Combine(images, "Route", "README.txt")));
        Assert.True(File.Exists(Path.Combine(game.RootPath, "TestGame", "PBZModLoader", "mods", "Route", "main.js")));

        manager.SetEnabled(mod.Id, false);

        Assert.False(Directory.Exists(Path.Combine(images, "Route")));

        manager.SetEnabled(mod.Id, true);

        Assert.True(File.Exists(Path.Combine(images, "Route", "icon.png")));

        manager.Uninstall(mod.Id);

        Assert.False(Directory.Exists(Path.Combine(images, "Route")));
        Assert.True(Directory.Exists(Path.Combine(game.RootPath, "TestGame", "Content", "Paks")));
    }

    [Fact]
    public void A_loose_file_of_the_game_in_the_content_folder_is_saved_and_put_back()
    {
        folder.File(@"Game\TestGame\Content\Movies\intro.mp4", "the game's own");
        folder.File(@"Downloads\Skip\TestGame\Content\Movies\intro.mp4", "a short one");

        var mod = Install(folder.Combine("Downloads", "Skip"));

        var movie = Path.Combine(game.RootPath, "TestGame", "Content", "Movies", "intro.mp4");
        Assert.Equal("a short one", File.ReadAllText(movie));

        manager.Uninstall(mod.Id);

        Assert.Equal("the game's own", File.ReadAllText(movie));
    }

    [Fact]
    public void A_readme_in_a_script_folder_is_not_installed()
    {
        folder.File(@"Downloads\Docs\TestGame\Content\JavaScript\Menu\main.js", "the menu");
        folder.File(@"Downloads\Docs\TestGame\Content\JavaScript\Menu\README.md", "a readme stops some loaders");

        using var preview = manager.Inspect(folder.Combine("Downloads", "Docs"));

        Assert.Equal(["TestGame/Content/JavaScript/Menu/main.js"], Assert.Single(preview.Packages).RelativePaths);
    }

    [Fact]
    public void Scripts_go_where_the_download_places_them_and_leave_with_the_mod()
    {
        var mod = Install(Download());

        Assert.True(File.Exists(Scripts("Menu", "bootstrap.js")));
        Assert.True(File.Exists(Scripts("Menu", "Mods", "Menu", "main.js")));
        Assert.True(File.Exists(Path.Combine(game.BinariesPath, "version.dll")));

        manager.SetEnabled(mod.Id, false);

        Assert.False(Directory.Exists(Scripts("Menu")));
        Assert.False(File.Exists(Path.Combine(game.BinariesPath, "version.dll")));

        manager.SetEnabled(mod.Id, true);

        Assert.True(File.Exists(Scripts("Menu", "Mods", "Menu", "main.js")));

        manager.Uninstall(mod.Id);

        Assert.False(Directory.Exists(Scripts("Menu")));
        Assert.True(Directory.Exists(Scripts()));
        Assert.True(Directory.Exists(Path.Combine(game.RootPath, "TestGame", "Content", "Paks")));
    }

    [Fact]
    public void Two_mods_share_a_script_folder_and_leave_no_empty_folder()
    {
        var menu = Install(Download());
        var addOn = Install(AddOn());

        manager.Uninstall(menu.Id);

        Assert.True(File.Exists(Scripts("Menu", "Mods", "AddOn", "main.js")));
        Assert.False(Directory.Exists(Scripts("Menu", "Mods", "Menu")));

        manager.Uninstall(addOn.Id);

        Assert.False(Directory.Exists(Scripts("Menu")));
    }

    [Fact]
    public void Turning_off_a_mod_leaves_no_empty_folder_in_a_shared_script_folder()
    {
        Install(Download());
        var addOn = Install(AddOn());

        manager.SetEnabled(addOn.Id, false);

        Assert.False(Directory.Exists(Scripts("Menu", "Mods", "AddOn")));
        Assert.True(File.Exists(Scripts("Menu", "Mods", "Menu", "main.js")));
    }

    [Fact]
    public void The_folders_of_other_game_files_stay()
    {
        folder.File(@"Downloads\Keys\TestGame\Saved\Input\Keys.sav", "the keys");
        var mod = Install(folder.Combine("Downloads", "Keys"));

        manager.Uninstall(mod.Id);

        Assert.True(Directory.Exists(Path.Combine(game.RootPath, "TestGame", "Saved", "Input")));
    }

    [Fact]
    public void A_script_of_the_game_is_saved_and_put_back()
    {
        folder.File(@"Game\TestGame\Content\JavaScript\Menu\bootstrap.js", "the game's own");

        var mod = Install(Download());

        Assert.Equal("the start", File.ReadAllText(Scripts("Menu", "bootstrap.js")));

        manager.Uninstall(mod.Id);

        Assert.Equal("the game's own", File.ReadAllText(Scripts("Menu", "bootstrap.js")));
    }
}
