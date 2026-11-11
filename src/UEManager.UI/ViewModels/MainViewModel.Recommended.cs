using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using UEManager.Core.Mods;

namespace UEManager.UI.ViewModels;

/// <summary>
/// The card at the top of the Mods page, apart from the player's own mods: the mod the app recommends for this game,
/// and an extra download below it. The manifest names them, so they can come and go without a new app version.
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>The manifest answered at this start, so its word on the recommended mods counts. Offline, the marks stay.</summary>
    private bool isRecommendedKnown;

    private RecommendedOfferViewModel? recommendedMain;

    private RecommendedOfferViewModel? recommendedExtra;

    public RecommendedOfferViewModel RecommendedMain => recommendedMain ??= new RecommendedOfferViewModel(this, isMain: true);

    public RecommendedOfferViewModel RecommendedExtra => recommendedExtra ??= new RecommendedOfferViewModel(this, isMain: false);

    /// <summary>The mod whose technical notes are open.</summary>
    [ObservableProperty]
    private RecommendedOfferViewModel? recommendedNotes;

    /// <summary>The mod whose thank-you is open.</summary>
    [ObservableProperty]
    private RecommendedOfferViewModel? recommendedThanks;

    public bool HasRecommended => RecommendedMain.IsVisible || RecommendedExtra.IsVisible;

    /// <summary>An installed mod on the card has a newer version: the Mods entry in the sidebar says so.</summary>
    public bool IsRecommendedUpdateAvailable => RecommendedMain.IsUpdateAvailable || RecommendedExtra.IsUpdateAvailable;

    private IEnumerable<ModItemViewModel> RecommendedItems => new[] { RecommendedMain.Item, RecommendedExtra.Item }.OfType<ModItemViewModel>();

    /// <summary>The card's text hangs on the mods and on UE4SS, like every summary of this view model.</summary>
    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        // A dialog opened or closed: "Switch game" in the title bar follows.
        SwitchGameCommand.NotifyCanExecuteChanged();
        if (string.IsNullOrEmpty(e.PropertyName))
        {
            recommendedMain?.Refresh();
            recommendedExtra?.Refresh();
        }
    }

    /// <summary>What the manifest said at this start: marks the mods, and reads the newest release of each.</summary>
    private async Task ReadRecommendedAsync(Ue4ssManifestAnswer answer)
    {
        isRecommendedKnown = true;
        RecommendedMain.Offer(answer.Recommended);
        RecommendedExtra.Offer(answer.Recommended?.Extra);
        RefreshMods();
        var manifest = new Uri(answer.Url);
        await Task.WhenAll(RecommendedMain.ReadReleaseAsync(Http, manifest), RecommendedExtra.ReadReleaseAsync(Http, manifest));
        OnPropertyChanged(string.Empty);
    }

    /// <summary>
    /// Before PBZModLoader goes in: the copy installed by hand, once many files, goes out when the player agrees, as only
    /// one entry DLL may be in the game. False stops the install.
    /// </summary>
    private async Task<bool> CleanOldModLoaderAsync(InstallPreview preview, string? id)
    {
        if (!ModManager.IsModLoader(preview, id) || manager!.FindOldModLoader() is not { } old)
            return true;
        var message = L.T("A PBZModLoader installed by hand is in the game. The manager puts in its own copy, so these files of the hand install go to a backup first:") + "\n\n" + string.Join("\n", old.Places)
            + "\n\n" + L.T("Your mods and mods.ini stay where they are. The removed files go to a backup folder in the manager's data.");
        if (old.SourceOnlyMods.Count > 0)
            message += "\n\n" + L.F($"These mods have only TypeScript source and will not run with this version: {string.Join(", ", old.SourceOnlyMods)}.");
        if (!await AskAsync(L.T("Upgrade PBZModLoader"), message, L.T("Clean up and install"), width: 640))
            return false;
        var cleaned = false;
        await RunAsync(L.T("Cleaning up…"), async () =>
        {
            await Task.Run(() => manager.RemoveOldModLoader(old));
            cleaned = true;
        });
        return cleaned;
    }

    /// <summary>The mods the manifest names, as the mod list finds them.</summary>
    private List<(string Id, string? Folder)> RecommendedWanted() =>
        new[] { RecommendedMain.Mod, RecommendedExtra.Mod }.OfType<RecommendedMod>().Select(m => (m.Repo, m.Folder)).ToList();

    /// <summary>Gives each marked mod its place on the card. Offline, the manifest's word is missing, and they take the places in order.</summary>
    private void PlaceRecommended(List<ModItemViewModel> marked)
    {
        var main = marked.FirstOrDefault(RecommendedMain.Owns);
        var extra = marked.FirstOrDefault(item => item != main && RecommendedExtra.Owns(item));
        var others = marked.Where(item => item != main && item != extra).ToList();
        main ??= others.FirstOrDefault();
        extra ??= others.FirstOrDefault(item => item != main);
        RecommendedMain.Place(main);
        RecommendedExtra.Place(extra);
    }
}
