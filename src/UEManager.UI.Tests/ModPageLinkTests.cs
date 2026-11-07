using System.IO;
using UEManager.Core.Games;
using UEManager.Core.Mods;
using UEManager.Core.Storage;
using UEManager.UI.Services;
using UEManager.UI.ViewModels;
using Xunit;

namespace UEManager.UI.Tests;

/// <summary>"Open mod page" in a mod's row and details: only for a mod whose download kept the mod site's file name.</summary>
public class ModPageLinkTests : IDisposable
{
    private const string SiteDownload = "Travel Anywhere 61 1.0.1 2026-10-29T13-42Z LbtEo64mS.zip";

    private readonly string folder = Path.Combine(Path.GetTempPath(), "uemm-mod-page-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(folder))
            Directory.Delete(folder, recursive: true);
    }

    private void With(string? modPageUrl, Action<MainViewModel> check) => UiThread.Run(() =>
    {
        var paths = new AppPaths(Path.Combine(folder, "Data"));
        paths.EnsureWritable();
        var profile = new GameProfile { Id = "test", DisplayName = "Test", ModPageUrl = modPageUrl };
        check(new MainViewModel(profile, paths, new ArtSet(null, null)));
    });

    private ModItemViewModel Row(MainViewModel model, string? source, bool recommended = false) =>
        new(model, new ModRecord { Id = "a", Name = "Travel Anywhere", SourceFileName = source, Recommended = recommended }, Path.Combine(folder, "library"));

    private ModDetailsViewModel Details(MainViewModel model, string? source, bool recommended = false) =>
        new(model, new ModRecord { Id = "a", Name = "Travel Anywhere", SourceFileName = source, Recommended = recommended }, [], [], folder);

    [Fact]
    public void A_download_from_the_mod_site_links_its_page() => With("https://example.test/game/mods/{id}", model =>
    {
        Assert.Equal("https://example.test/game/mods/61", Row(model, SiteDownload).ModPageUrl);
        Assert.False(Row(model, SiteDownload).ShowsNoModPage);
        Assert.Equal("https://example.test/game/mods/61", Details(model, SiteDownload).ModPageUrl);
    });

    [Fact]
    public void Another_download_has_no_link_and_says_so() => With("https://example.test/game/mods/{id}", model =>
    {
        Assert.Null(Row(model, "Travel Anywhere.zip").ModPageUrl);
        Assert.True(Row(model, "Travel Anywhere.zip").ShowsNoModPage);
        Assert.Null(Row(model, null).ModPageUrl);
        Assert.Null(Details(model, "PBZModLoader-2.1.0-win64.zip").ModPageUrl);
    });

    [Fact]
    public void A_mod_on_the_card_shows_neither_even_from_the_mod_site() => With("https://example.test/game/mods/{id}", model =>
    {
        Assert.Null(Row(model, SiteDownload, recommended: true).ModPageUrl);
        Assert.False(Row(model, SiteDownload, recommended: true).ShowsNoModPage);
        Assert.False(Row(model, "PBZModLoader-2.1.0-win64.zip", recommended: true).ShowsNoModPage);
        Assert.Null(Details(model, SiteDownload, recommended: true).ModPageUrl);
    });

    [Fact]
    public void A_game_without_a_page_address_has_no_link() => With(null, model =>
    {
        Assert.Null(Row(model, SiteDownload).ModPageUrl);
        Assert.Null(Details(model, SiteDownload).ModPageUrl);
    });
}
