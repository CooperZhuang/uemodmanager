using UEManager.Core.Games;
using UEManager.Core.Mods;
using UEManager.Core.Storage;

namespace UEManager.Core.Tests;

/// <summary>
/// A mod the player put into ~mods by hand, often in a folder of its own as many readmes say, is not installed a second
/// time: the game mounts packages from every folder below ~mods, so it would be in the game twice.
/// </summary>
public class HandPlacedCopyTests : IDisposable
{
    private readonly TestFolder folder = new();
    private readonly GameInstall game;
    private readonly ModManager manager;

    public HandPlacedCopyTests()
    {
        game = GameLocator.Resolve(TestFolder.Profile(), folder.Game(), GameStore.Manual)!;
        manager = new ModManager(game, new AppPaths(folder.Combine("Data")), _ => false);
    }

    public void Dispose() => folder.Dispose();

    private string Download(string name)
    {
        foreach (var extension in new[] { ".pak", ".utoc", ".ucas" })
            folder.File($@"Downloads\{name}\{name}_P{extension}", "v1");
        return folder.Combine("Downloads", name);
    }

    private InstallCheck Check(string source, string? ownFolder = null)
    {
        using var preview = manager.Inspect(source);
        return manager.Check(preview, preview.Packages.ToList(), null, ownFolder);
    }

    private void ByHand(string relativePath)
    {
        var path = Path.Combine(game.ModsPath, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "by hand");
    }

    [Fact]
    public void A_copy_in_a_folder_of_its_own_is_named_and_stops_the_install()
    {
        var source = Download("Skin");
        ByHand(@"Skin_P\Skin_P.pak");
        ByHand(@"Skin_P\Skin_P.utoc");
        ByHand(@"Skin_P\Skin_P.ucas");

        var check = Check(source);

        Assert.False(check.CanInstall);
        Assert.Equal($"3 files of this download are already in {Path.Combine("~mods", "Skin_P")}, but the manager did not install them: "
            + "Skin_P.pak, Skin_P.utoc, Skin_P.ucas. Move or delete them first.", Assert.Single(check.Problems).ToString());
    }

    [Fact]
    public void A_copy_straight_in_the_mods_folder_stops_a_install_into_a_folder_of_its_own()
    {
        var source = Download("Skin");
        ByHand("Skin_P.pak");

        var check = Check(source, ownFolder: "Skin");

        Assert.Equal("Skin_P.pak is already in ~mods, but the manager did not install it. Move or delete it first.",
            Assert.Single(check.Problems).ToString());
    }

    [Fact]
    public void Copies_the_manager_placed_and_other_files_do_not_count()
    {
        // Another mod with the same file names, kept beside it in a folder of its own by the player's choice.
        using (var preview = manager.Inspect(Download("Skin")))
            manager.Install(preview, preview.Packages.ToList(), "Skin", null, ownFolder: "Skin");
        ByHand(@"Notes\Skin_P.txt");
        ByHand(@"Other\Other_P.pak");

        var check = Check(Download("Skin"), ownFolder: "Skin 2");

        Assert.True(check.CanInstall, string.Join(" | ", check.Problems));
    }
}
