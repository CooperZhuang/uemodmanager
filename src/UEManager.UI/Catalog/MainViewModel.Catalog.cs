using System.IO;
using UEManager.Core;
using UEManager.Core.Catalog;
using UEManager.Core.Mods;
using UEManager.UI.Catalog;
using UEManager.UI.Services;

namespace UEManager.UI.ViewModels;

/// <summary>
/// The openmods page's part of the main view model. While the catalog is switched off
/// (<see cref="AppInfo.IsCatalogOn"/>), it makes no catalog, so the page never goes online.
/// </summary>
public sealed partial class MainViewModel
{
    public CatalogViewModel? Catalog { get; private set; }

    partial void CreateCatalog(string? catalogUrl)
    {
        if (AppInfo.IsCatalogOn)
            Catalog = new CatalogViewModel(this, Http, catalogUrl ?? OpenMods.GamesListUrl, Profile.SteamAppId, isTest: catalogUrl is not null,
                Enum.TryParse<CatalogSortField>(settings.CatalogSort, out var sort) ? sort : CatalogSortField.Updated,
                settings.CatalogSort is null || settings.CatalogSortDescending);
    }

    partial void OnModsChanged() => Catalog?.UpdateInstalled(manager?.Mods ?? []);

    partial void OnSelectedPageChanged(string value)
    {
        if (value == "Catalog")
            _ = Catalog?.LoadAsync();
    }

    internal void SaveCatalogSort(CatalogSortField field, bool descending)
    {
        settings.CatalogSort = field.ToString();
        settings.CatalogSortDescending = descending;
        SaveSettings();
    }

    /// <summary>
    /// One click on the openmods page, or on the update box of a mod installed from there: see
    /// <see cref="InstallFromReleaseAsync"/>. The mod's page on openmods goes with it.
    /// </summary>
    internal async Task InstallFromCatalogAsync(CatalogMod mod)
    {
        if (mod.Release is not { } release || mod.InstallFile is not { } file)
            return;
        await InstallFromReleaseAsync(mod.Id, mod.Name, release.Version, [file.Url], file.Name, file.Size, file.Sha256);
        RememberCatalogPages();
    }

    /// <summary>
    /// The game's folders for reading a release zip's paths as an install would (see <see cref="ModManager.ScanFolders"/>);
    /// before the game is found, only the profile's project folder.
    /// </summary>
    internal (string? ProjectFolder, string? ExeFolder) CatalogScanFolders =>
        manager?.ScanFolders is { } folders ? (folders.ProjectFolder, folders.ExeFolder) : (Profile.ProjectFolder, null);

    /// <summary>The mods of the last openmods list, by id: each one's page, newest release and one-click install.</summary>
    private readonly Dictionary<string, CatalogMod> catalogMods = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>A mod the player installed from openmods; the card's mods have an offer of their own.</summary>
    internal static bool IsFromCatalog(ModRecord mod) => mod.CatalogId is not null && !mod.Recommended;

    /// <summary>The openmods entry of a mod installed from there, once the list is read; null for any other mod.</summary>
    internal CatalogMod? CatalogModOf(ModRecord mod) =>
        IsFromCatalog(mod) && catalogMods.TryGetValue(mod.CatalogId!, out var entry) ? entry : null;

    /// <summary>The mod's page on openmods: from the list once it is read, else as stored at the install; null for other mods.</summary>
    internal string? CatalogPageOf(ModRecord mod) => IsFromCatalog(mod) ? CatalogModOf(mod)?.Page ?? mod.CatalogPage : null;

    /// <summary>The mod's page (see <see cref="ModPageOf"/>) is on openmods: its link reads "openmods.nexus", in orange.</summary>
    internal bool IsOpenModsPage(ModRecord mod) => CatalogPageOf(mod) is not null;

    /// <summary>The words of a mod's page link on the Mods page: the site's name for a page on openmods, so it stands apart.</summary>
    internal static string ModPageText(bool isOpenModsPage) => isOpenModsPage ? CatalogViewModel.SiteName : L.T("Open mod page");

    /// <summary>
    /// The update box of a mod installed from openmods: its version against the newest release, by the openmods page's
    /// own rule (<see cref="ModVersion.IsUpdate"/>), so both pages agree. Null before the list is read.
    /// </summary>
    private ModUpdateStatus? CatalogUpdateOf(ModRecord mod) =>
        CatalogModOf(mod)?.Release is { } release
            ? new ModUpdateStatus(ModVersion.IsUpdate(mod.Version, release.Version), release.Version, mod.Version ?? "—")
            : null;

    /// <summary>The openmods page read its list: the Mods page's rows of mods installed from it get their page and update box.</summary>
    internal void ShowCatalogList(CatalogList? list)
    {
        catalogMods.Clear();
        foreach (var mod in list?.Mods ?? [])
            catalogMods.TryAdd(mod.Id, mod);
        RememberCatalogPages();
        RefreshMods();
    }

    /// <summary>Stores the page of each mod installed from openmods as the list gives it, so its row links there without the list.</summary>
    private void RememberCatalogPages()
    {
        if (manager is null)
            return;
        foreach (var mod in manager.Mods.Where(IsFromCatalog).ToList())
        {
            if (CatalogModOf(mod)?.Page is not { } page || page == mod.CatalogPage)
                continue;
            try
            {
                manager.SetCatalogPage(mod.Id, page);
            }
            catch (Exception ex) when (ex is ModException or IOException or UnauthorizedAccessException)
            {
                Log.Warn($"The openmods page of \"{mod.Name}\" could not be stored: {ex.Message}");
            }
        }
    }
}
