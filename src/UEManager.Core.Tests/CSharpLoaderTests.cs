using UEManager.Core.Games;
using UEManager.Core.Mods;
using UEManager.Core.Storage;

namespace UEManager.Core.Tests;

/// <summary>
/// Black Myth: Wukong's C# Loader (KintaroModding/b1csharploader): a version.dll next to the exe and the folder
/// CSharpLoader beside it. Its mods go into CSharpLoader/Mods/&lt;mod&gt;, wait for the loader, and keep their settings.
/// </summary>
public class CSharpLoaderTests : IDisposable
{
    private readonly TestFolder folder = new();
    private readonly ModManager manager;
    private readonly GameInstall game;

    public CSharpLoaderTests()
    {
        game = GameLocator.Resolve(TestFolder.Profile("b1"), folder.Game("b1"), GameStore.Manual)!;
        manager = new ModManager(game, new AppPaths(folder.Combine("Data")));
    }

    public void Dispose() => folder.Dispose();

    /// <summary>The loader's release zip, as the user's repo packs it.</summary>
    private string Loader()
    {
        folder.File(@"Downloads\Loader\README.md", "the loader's readme");
        folder.File(@"Downloads\Loader\b1\Binaries\Win64\version.dll", "the entry");
        folder.File(@"Downloads\Loader\b1\Binaries\Win64\CSharpLoader\CSharpManager.bin", "the manager");
        folder.File(@"Downloads\Loader\b1\Binaries\Win64\CSharpLoader\0Harmony.dll", "hooks");
        folder.File(@"Downloads\Loader\b1\Binaries\Win64\CSharpLoader\b1cs.ini", "[Settings]\r\nConsole=0\r\n");
        return folder.Combine("Downloads", "Loader");
    }

    /// <summary>A C# mod as the mod site has it: the game's folders below a top folder, and the note of openmods.nexus.</summary>
    private string CSharpMod()
    {
        folder.File(@"Downloads\Mod\faster_1.0\openmods.nexus_opensourcemods.txt", "see openmods.nexus");
        folder.File(@"Downloads\Mod\faster_1.0\b1\Binaries\Win64\CSharpLoader\Mods\faster\faster.dll", "the mod");
        folder.File(@"Downloads\Mod\faster_1.0\b1\Binaries\Win64\CSharpLoader\Mods\faster\config.json", "{ \"rate\": 2 }");
        return folder.Combine("Downloads", "Mod");
    }

    private InstallCheck Check(string source)
    {
        using var preview = manager.Inspect(source);
        return manager.Check(preview, preview.Packages.ToList());
    }

    private ModRecord Install(string source)
    {
        using var preview = manager.Inspect(source);
        return manager.Install(preview, preview.Packages.ToList(), preview.SuggestedName, preview.SuggestedVersion);
    }

    [Fact]
    public void A_CSharp_mod_waits_for_the_loader()
    {
        var mod = CSharpMod();

        Assert.False(manager.IsCSharpLoaderInstalled);
        var check = Check(mod);
        Assert.False(check.CanInstall);
        Assert.Equal("This mod runs in C# Loader, which is not installed. Install it first from the top of the Mods page, then this mod.",
            Assert.Single(check.Problems).ToString());

        Install(Loader());

        Assert.True(manager.IsCSharpLoaderInstalled);
        Assert.True(Check(mod).CanInstall);
        var record = Install(mod);
        Assert.True(File.Exists(Path.Combine(game.BinariesPath, "CSharpLoader", "Mods", "faster", "faster.dll")));
        // The note of openmods.nexus is a document, as a readme: it stays out.
        Assert.DoesNotContain(record.Files, f => f.FileName.Contains("openmods", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_loader_put_in_by_hand_counts()
    {
        folder.File(Path.Combine(game.BinariesPath, "version.dll"), "the entry");
        folder.File(Path.Combine(game.BinariesPath, "CSharpLoader", "CSharpManager.bin"), "the manager");

        Assert.True(manager.IsCSharpLoaderInstalled);
        Assert.True(Check(CSharpMod()).CanInstall);
    }

    [Fact]
    public void A_download_with_the_loader_and_a_mod_needs_nothing_else()
    {
        Loader();
        folder.File(@"Downloads\Loader\b1\Binaries\Win64\CSharpLoader\Mods\Example\Example.dll", "an example mod");

        Assert.True(Check(folder.Combine("Downloads", "Loader")).CanInstall);
    }

    [Fact]
    public void The_loader_settings_and_a_mod_config_are_the_player_settings()
    {
        var loader = Install(Loader());

        Assert.Equal(["b1/Binaries/Win64/CSharpLoader/b1cs.ini"], loader.Files.Where(f => f.IsSettingsFile).Select(f => f.FileName));
        Assert.True(SettingsFiles.Is(DeployTarget.InstallRoot, "b1/Binaries/Win64/CSharpLoader/Mods/faster/config.json"));
        Assert.True(SettingsFiles.Is(DeployTarget.ExeFolder, "CSharpLoader/Mods/faster/settings.ini"));
        Assert.True(SettingsFiles.Is(DeployTarget.ExeFolder, "CSharpLoader/b1cs.ini"));
        // The mod's code, the loader's own files and a mod's readme are none.
        Assert.False(SettingsFiles.Is(DeployTarget.InstallRoot, "b1/Binaries/Win64/CSharpLoader/Mods/faster/faster.dll"));
        Assert.False(SettingsFiles.Is(DeployTarget.InstallRoot, "b1/Binaries/Win64/CSharpLoader/CSharpManager.bin"));
        Assert.False(SettingsFiles.Is(DeployTarget.ExeFolder, "CSharpLoader/Mods/faster/readme.txt"));
        Assert.False(SettingsFiles.Is(DeployTarget.InstallRoot, "b1/Binaries/Win64/other.ini"));
    }

    [Fact]
    public void A_CSharp_mod_for_the_exe_folder_is_ticked_as_the_place_is_known()
    {
        folder.File(@"Downloads\Bare\CSharpLoader\Mods\faster\faster.dll", "the mod");
        using var preview = manager.Inspect(folder.Combine("Downloads", "Bare"));

        var part = Assert.Single(preview.Packages);
        Assert.Equal(DeployTarget.ExeFolder, part.Target);
        Assert.False(part.IsUncertain);
        Assert.Equal(PackageScanner.ForCSharpLoader, part.NeedsLoader);
    }
}
