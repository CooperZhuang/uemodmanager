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

    /// <summary>One click on the openmods page: see <see cref="InstallFromReleaseAsync"/>.</summary>
    internal Task InstallFromCatalogAsync(CatalogMod mod) =>
        mod.Release is { } release && mod.InstallFile is { } file
            ? InstallFromReleaseAsync(mod.Id, mod.Name, release.Version, [file.Url], file.Name, file.Size, file.Sha256)
            : Task.CompletedTask;
}
