using System.IO;
using UEManager.Core.Games;
using UEManager.Core.Storage;
using UEManager.UI.Services;
using UEManager.UI.ViewModels;
using Xunit;

namespace UEManager.UI.Tests;

/// <summary>The window that lists the files in ~mods that no mod placed.</summary>
public sealed class UnmanagedFilesTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "uemm-unmanaged-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(folder))
            Directory.Delete(folder, recursive: true);
    }

    [Fact]
    public void The_files_show_in_the_tree_of_their_folders_with_their_sizes()
    {
        var mods = Path.Combine(folder, "TestGame", "Content", "Paks", "~mods");
        Directory.CreateDirectory(Path.Combine(mods, "Hand"));
        File.WriteAllText(Path.Combine(mods, "Loose_P.pak"), "12345");
        File.WriteAllText(Path.Combine(mods, "Hand", "Skin_P.pak"), "123");
        string? opened = null;

        var rows = MainViewModel.UnmanagedTree(["Loose_P.pak", Path.Combine("Hand", "Skin_P.pak")], mods, folder, place => opened = place);

        var top = rows[0];
        Assert.True(top.IsFolder);
        Assert.EndsWith("~mods", top.Name);
        Assert.Contains(rows, row => row.IsFolder && row.Name == "Hand");
        var loose = Assert.Single(rows, row => row.Name == "Loose_P.pak");
        Assert.Equal(Format.Size(5), loose.File!.Size);
        Assert.Equal(Path.Combine(mods, "Loose_P.pak"), loose.File.FullPath);
        Assert.Equal(Format.Size(3), Assert.Single(rows, row => row.Name == "Skin_P.pak").File!.Size);

        top.OpenFolder!.Execute(null);
        Assert.Equal(mods, opened);
    }

    [Fact]
    public void A_file_gone_since_it_was_counted_shows_without_a_size()
    {
        var rows = MainViewModel.UnmanagedTree(["Gone_P.pak"], Path.Combine(folder, "mods"), folder);

        Assert.Equal("", Assert.Single(rows, row => row.Name == "Gone_P.pak").File!.Size);
    }

    [Fact]
    public void Without_a_game_the_window_stays_closed_and_close_closes_it() => UiThread.Run(() =>
    {
        var paths = new AppPaths(Path.Combine(folder, "Data"));
        paths.EnsureWritable();
        var model = new MainViewModel(new GameProfile { Id = "test", DisplayName = "Phantom Blade Zero" }, paths, new ArtSet(null, null));

        model.ShowUnmanagedCommand.Execute(null);
        Assert.False(model.IsUnmanagedOpen);

        model.UnmanagedRows = [];
        Assert.True(model.IsUnmanagedOpen);
        Assert.True(model.HasOverlay);

        model.CloseUnmanagedCommand.Execute(null);
        Assert.False(model.IsUnmanagedOpen);
        Assert.False(model.HasOverlay);
    });
}
