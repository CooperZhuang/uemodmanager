using System.IO;
using UEManager.Core.Games;
using UEManager.Core.Mods;
using UEManager.Core.Storage;
using UEManager.UI.ViewModels;
using Xunit;

namespace UEManager.UI.Tests;

/// <summary>A loader's download with other entry DLLs: only one entry may be in the game, so the review ticks one.</summary>
public sealed class InstallDialogEntryTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "uemm-entries-" + Guid.NewGuid().ToString("N"));

    public void Dispose() => Directory.Delete(root, recursive: true);

    private string File(string relative)
    {
        var path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        System.IO.File.WriteAllText(path, relative);
        return path;
    }

    private ModManager Manager()
    {
        File(@"Game\Engine\Binaries\Win64\placeholder");
        File(@"Game\TestGame\Content\Paks\placeholder");
        File(@"Game\TestGame\Binaries\Win64\TestGame-Win64-Shipping.exe");
        File(@"Game\TestGame.exe");
        var profile = new GameProfile { Id = "test-game", DisplayName = "Test Game", SteamAppId = "999999", ProjectFolder = "TestGame" };
        var game = GameLocator.Resolve(profile, Path.Combine(root, "Game"), GameStore.Manual)!;
        return new ModManager(game, new AppPaths(Path.Combine(root, "Data")));
    }

    [Fact]
    public void A_zip_for_two_loaders_waits_for_a_loader_and_then_keeps_the_choice()
    {
        File(@"Markers\TestGame\PBZModLoader\mods\Markers\main.js");
        File(@"Markers\TestGame\Content\JavaScript\PBZModsMenu\Mods\Markers\main.js");
        var manager = Manager();
        using (var preview = manager.Inspect(Path.Combine(root, "Markers")))
        {
            var dialog = new InstallDialogViewModel(manager, preview, () => { });

            Assert.Equal("PBZModLoader", Assert.Single(dialog.Packages, p => p.IsSelected).Label);
            Assert.False(dialog.InstallCommand.CanExecute(null));

            File(@"Game\TestGame\Content\JavaScript\PBZModsMenu\bootstrap.js");
            var menu = Assert.Single(dialog.Packages, p => p.Label == "Mods Menu");
            menu.IsSelected = true;
            Assert.True(dialog.InstallCommand.CanExecute(null));
            Assert.Single(dialog.Packages, p => p.IsSelected);

            manager.Install(preview, [menu.Package], "Markers", null);
        }

        using var next = manager.Inspect(Path.Combine(root, "Markers"));
        var update = new InstallDialogViewModel(manager, next, () => { });

        Assert.Equal("Mods Menu", Assert.Single(update.Packages, p => p.IsSelected).Label);
    }

    [Fact]
    public void A_zip_for_two_loaders_ticks_the_copy_for_the_loader_in_the_game()
    {
        File(@"Markers\TestGame\PBZModLoader\mods\Markers\main.js");
        File(@"Markers\TestGame\Content\JavaScript\PBZModsMenu\Mods\Markers\main.js");
        System.IO.File.WriteAllText(File(@"Game\TestGame\Binaries\Win64\version.dll"), "[PBZMOD]");
        var manager = Manager();
        using var preview = manager.Inspect(Path.Combine(root, "Markers"));

        var dialog = new InstallDialogViewModel(manager, preview, () => { });

        Assert.Equal("PBZModLoader", Assert.Single(dialog.Packages, p => p.IsSelected).Label);
        Assert.True(dialog.InstallCommand.CanExecute(null));
    }

    [Fact]
    public void The_review_ticks_the_usual_entry_only_and_a_choice_clears_it()
    {
        File(@"Loader\TestGame\Binaries\Win64\version.dll");
        foreach (var name in new[] { "dsound", "dxgi", "hid", "winmm" })
            File($@"Loader\alternative-entries\{name}\{name}.dll");
        var manager = Manager();
        using var preview = manager.Inspect(Path.Combine(root, "Loader"));

        var dialog = new InstallDialogViewModel(manager, preview, () => { });

        Assert.Equal(5, dialog.Packages.Count);
        var ticked = Assert.Single(dialog.Packages, p => p.IsSelected);
        Assert.Equal("version.dll", ticked.Package.Name);

        var hid = Assert.Single(dialog.Packages, p => p.Package.Name == "hid");
        Assert.False(hid.IsUncertain);
        hid.IsSelected = true;

        Assert.Same(hid, Assert.Single(dialog.Packages, p => p.IsSelected));
    }
}
