using System.IO;
using UEManager.UI.ViewModels;
using Xunit;

namespace UEManager.UI.Tests;

/// <summary>A mod's files in the tree of their folders, as the details window shows them.</summary>
public class ModTreeTests
{
    private static ModFileItem File(string place) => new(place, @"C:\Games\PBZ\" + place, "1 KB", "In the game", "good");

    [Fact]
    public void Folders_come_first_and_a_folder_with_one_folder_shares_its_line()
    {
        var rows = ModDetailsViewModel.Tree(
        [
            File(@"platform\steam\ue4ss\Mods\ModConfigUI\Scripts\main.lua"),
            File(@"platform\steam\ue4ss\Mods\ModConfigUI\enabled.txt"),
            File(@"platform\steam\ue4ss\Mods\shared\mcu_core\schema.lua"),
            File(@"platform\steam\ue4ss\Mods\shared\json\json.lua"),
            File(@"platform\steam\ue4ss\Mods\ModConfigUI\Scripts\config_page.lua"),
        ]);

        Assert.Equal(
            [
                (0, @"platform\steam\ue4ss\Mods", true),
                (1, "ModConfigUI", true),
                (2, "Scripts", true),
                (3, "config_page.lua", false),
                (3, "main.lua", false),
                (2, "enabled.txt", false),
                (1, "shared", true),
                (2, "json", true),
                (3, "json.lua", false),
                (2, "mcu_core", true),
                (3, "schema.lua", false),
            ],
            rows.Select(r => (r.Depth, r.Name, r.IsFolder)));
        Assert.Equal(@"C:\Games\PBZ\platform\steam\ue4ss\Mods\ModConfigUI\Scripts\main.lua", rows[4].File!.FullPath);
        Assert.All(rows, r => Assert.Null(r.OpenFolder));
    }

    [Fact]
    public void In_the_mod_details_each_folder_opens_its_folder_in_the_game()
    {
        var opened = new List<string>();
        var rows = ModDetailsViewModel.Tree(
            [File(@"platform\steam\ue4ss\Mods\Witch\Scripts\main.lua"), File(@"platform\steam\ue4ss\Mods\Witch\enabled.txt")],
            @"C:\Games\PBZ", opened.Add);

        Assert.Equal([@"C:\Games\PBZ\platform\steam\ue4ss\Mods\Witch", @"C:\Games\PBZ\platform\steam\ue4ss\Mods\Witch\Scripts"],
            rows.Where(r => r.IsFolder).Select(r => r.FolderPath));
        Assert.All(rows.Where(r => !r.IsFolder), r => Assert.Null(r.OpenFolder));
        rows[1].OpenFolder!.Execute(null);
        Assert.Equal([@"C:\Games\PBZ\platform\steam\ue4ss\Mods\Witch\Scripts"], opened);
    }

    [Fact]
    public void A_folder_that_is_gone_opens_the_nearest_one_above_it_but_never_above_the_game()
    {
        var game = Directory.CreateTempSubdirectory("uemm-tree-").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(game, "ue4ss", "Mods"));

            Assert.Equal(Path.Combine(game, "ue4ss", "Mods"), ModDetailsViewModel.NearestFolder(Path.Combine(game, "ue4ss", "Mods", "Witch", "Scripts"), game));
            Assert.Equal(game, ModDetailsViewModel.NearestFolder(Path.Combine(game, "gone"), game + Path.DirectorySeparatorChar));
        }
        finally
        {
            Directory.Delete(game, recursive: true);
        }
    }

    [Fact]
    public void A_pak_mod_is_one_folder_line_with_its_files()
    {
        var rows = ModDetailsViewModel.Tree(
        [
            File(@"ProjectPBZ\Content\Paks\~mods\zzz_Mod_P.utoc"),
            File(@"ProjectPBZ\Content\Paks\~mods\zzz_Mod_P.pak"),
        ]);

        Assert.Equal(
            [(0, @"ProjectPBZ\Content\Paks\~mods"), (1, "zzz_Mod_P.pak"), (1, "zzz_Mod_P.utoc")],
            rows.Select(r => (r.Depth, r.Name)));
    }
}
