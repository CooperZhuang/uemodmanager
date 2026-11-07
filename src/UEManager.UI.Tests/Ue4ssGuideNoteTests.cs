using System.IO;
using UEManager.Core.Games;
using UEManager.Core.Mods;
using UEManager.Core.Storage;
using UEManager.UI.Services;
using UEManager.UI.ViewModels;
using Xunit;

namespace UEManager.UI.Tests;

/// <summary>The game profile's note in the Install UE4SS window: needed only when the player downloads UE4SS by hand.</summary>
public sealed class Ue4ssGuideNoteTests : IDisposable
{
    private const string NoteText = "Use the UE4SS release made for Phantom Blade Zero. It adds the files UE4SS needs to read this game's structures.";

    private readonly string root = Path.Combine(Path.GetTempPath(), "uemm-guide-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }

    private void File(string relative)
    {
        var path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        System.IO.File.WriteAllText(path, relative);
    }

    private Ue4ssGuideViewModel Guide(Ue4ssRelease? release)
    {
        File(@"Game\Engine\Binaries\Win64\placeholder");
        File(@"Game\TestGame\Content\Paks\placeholder");
        File(@"Game\TestGame\Binaries\Win64\TestGame-Win64-Shipping.exe");
        File(@"Game\TestGame.exe");
        var profile = new GameProfile { Id = "test-game", DisplayName = "Test Game", ProjectFolder = "TestGame", Ue4ssNote = NoteText };
        var game = GameLocator.Resolve(profile, Path.Combine(root, "Game"), GameStore.Manual)!;
        var paths = new AppPaths(Path.Combine(root, "Data"));
        paths.EnsureWritable();
        var owner = new MainViewModel(profile, paths, new ArtSet(null, null));
        return new Ue4ssGuideViewModel(owner, game, release);
    }

    [Fact]
    public void With_the_one_click_install_the_note_stays_away() => UiThread.Run(() =>
    {
        var guide = Guide(new Ue4ssRelease("3.0.1", "https://example.com/ue4ss.zip", null));

        Assert.True(guide.CanDownload);
        Assert.Null(guide.Note);
    });

    [Fact]
    public void For_a_download_by_hand_the_note_shows() => UiThread.Run(() =>
    {
        var guide = Guide(null);

        Assert.False(guide.CanDownload);
        Assert.Equal(NoteText, guide.Note);
    });
}
