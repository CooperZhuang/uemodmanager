using System.Net.Http;
using CommunityToolkit.Mvvm.Input;
using UEManager.Core;
using UEManager.Core.Mods;
using UEManager.Core.Updates;

namespace UEManager.UI.ViewModels;

/// <summary>A link in a thank-you: as it reads, and where it goes.</summary>
public sealed record ThanksLink(string Text, string Url);

/// <summary>
/// One mod on the card at the top of the Mods page: the recommended mod, or the extra download below it. The manifest
/// names it, and its newest release is read at every start. Once installed, it has the row every mod has.
/// </summary>
public sealed partial class RecommendedOfferViewModel(MainViewModel owner, bool isMain) : LocalizedObservableObject
{
    private RecommendedMod? mod;

    /// <summary>The newest release of the mod's repo; null while it is read, or when it could not be read.</summary>
    private AppRelease? release;

    private bool isReleaseRead;

    /// <summary>The installed mod, with the row every mod has.</summary>
    public ModItemViewModel? Item { get; private set; }

    /// <summary>What the manifest says; null when it names no mod, or has not answered.</summary>
    internal RecommendedMod? Mod => mod;

    public bool IsMain => isMain;

    public bool IsExtra => !isMain;

    public bool IsVisible => mod is not null || Item is not null;

    public bool IsInstalled => Item is not null;

    /// <summary>Not installed yet: the row, with the mod's name, is not there.</summary>
    public bool ShowName => Item is null && mod is not null;

    /// <summary>The label above the mod: an experimental mod says so, from the manifest.</summary>
    public string Label => (isMain, mod?.Experimental == true) switch
    {
        (true, true) => L.T("Recommended - Experimental"),
        (true, false) => L.T("Recommended"),
        (false, true) => L.T("Also available - Experimental"),
        (false, false) => L.T("Also available"),
    };

    public string? Name => mod?.Name;

    public string? Text => mod?.TextIn(L.Current.Language);

    public bool HasText => !string.IsNullOrWhiteSpace(Text);

    /// <summary>The technical notes in the chosen language, as paragraphs; the manifest may have none.</summary>
    public IReadOnlyList<string> Notes =>
        mod?.NotesIn(L.Current.Language)?.Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];

    public bool HasNotes => Notes.Count > 0;

    /// <summary>Where the mod's source code is, as the notes show it.</summary>
    public string? Source => mod is null ? null : $"github.com/{mod.Repo}";

    /// <summary>The green box's text; the manifest may have no thank-you.</summary>
    public string? ThanksBoxText => mod?.Thanks?.BoxIn(L.Current.Language);

    public bool HasThanks => !string.IsNullOrWhiteSpace(ThanksBoxText);

    /// <summary>The thank-you in the chosen language, as paragraphs.</summary>
    public IReadOnlyList<string> ThanksParagraphs =>
        mod?.Thanks?.TextIn(L.Current.Language)?.Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];

    public IReadOnlyList<ThanksLink> ThanksLinks =>
        mod?.Thanks?.Links.Select(url => new ThanksLink(new Uri(url).Host + new Uri(url).AbsolutePath, url)).ToList() ?? [];

    /// <summary>A click also installs or updates UE4SS first: such a mod may work only with the UE4SS the manifest names.</summary>
    private bool NeedsUe4ssFirst => mod is { NeedsUe4ss: true } && owner.AvailableUe4ss is not null
        && (!owner.IsUe4ssInstalled || owner.IsUe4ssUpdateAvailable);

    /// <summary>The installed mod has a newer version: the card offers it, and the Mods entry in the sidebar says so.</summary>
    public bool IsUpdateAvailable => mod is not null && release is { File: not null }
        && Item is not null && ModVersion.IsUpdate(Item.Record.Version, release.Version);

    /// <summary>The button: install or update, with UE4SS when it needs it; none when there is nothing to do or no file to get.</summary>
    public string? ActionText
    {
        get
        {
            if (mod is null || release is not { File: not null })
                return null;
            if (Item is not null)
            {
                if (!IsUpdateAvailable)
                    return null;
                return NeedsUe4ssFirst ? L.T("Update UE4SS and this mod") : L.F($"Update to {release.Version}");
            }
            return !NeedsUe4ssFirst ? L.T("Download and install")
                : owner.IsUe4ssInstalled ? L.T("Update UE4SS and install this mod")
                : L.T("Install UE4SS and this mod");
        }
    }

    public bool HasMainAction => isMain && ActionText is not null;

    public bool HasExtraAction => !isMain && ActionText is not null;

    /// <summary>The newest version, behind the name of a mod that is not installed yet.</summary>
    public string? VersionText => Item is null && release is { File: not null } ? L.F($"Version {release.Version}") : null;

    public bool HasVersion => VersionText is not null;

    /// <summary>A short line under the mod's text: why there is no version or button yet.</summary>
    public string? StatusText
    {
        get
        {
            if (mod is null)
                return null;
            if (!isReleaseRead)
                return L.T("Looking for the newest version…");
            return release is { File: not null } ? null : L.T("The download cannot be reached right now. The app tries again at the next start.");
        }
    }

    public bool HasStatus => StatusText is not null;

    /// <summary>The line under the extra mod's row while the main mod does its job as well; the manifest may have none.</summary>
    public string? RetireText => mod?.RetireTextIn(L.Current.Language);

    public string? RetireButtonText => mod?.RetireButtonIn(L.Current.Language);

    public bool ShowRetire => !isMain && Item is not null && owner.RecommendedMain.IsInstalled
        && !string.IsNullOrWhiteSpace(RetireText) && !string.IsNullOrWhiteSpace(RetireButtonText);

    /// <summary>What the manifest said at this start. The release is read after it.</summary>
    internal void Offer(RecommendedMod? offered)
    {
        mod = offered;
        release = null;
        isReleaseRead = false;
        Refresh();
    }

    internal void Place(ModItemViewModel? item) => Item = item;

    internal void Refresh() => OnPropertyChanged(string.Empty);

    /// <summary>Whether this is the installed copy of the mod the manifest names.</summary>
    internal bool Owns(ModItemViewModel item) =>
        mod is not null && string.Equals(item.Record.CatalogId, mod.Repo, StringComparison.OrdinalIgnoreCase);

    /// <summary>Reads the newest release: first the copy next to the manifest, on the host that just answered, then GitHub.</summary>
    internal async Task ReadReleaseAsync(HttpClient http, Uri manifestUrl)
    {
        if (mod is not { } offered)
            return;
        var copies = new Uri(manifestUrl, $"mods/{offered.RepoName}/");
        try
        {
            var read = await AppReleases.FetchLatestAsync(http,
                [new Uri(copies, "latest.json").AbsoluteUri, $"https://api.github.com/repos/{offered.Repo}/releases/latest"],
                offered.FilePrefix, fileSuffix: offered.FileSuffix);
            // GitHub's answer names only its own file: the copy is the way on when that download fails.
            release = read.File is { FallbackUrl: null } file
                ? read with { File = file with { FallbackUrl = new Uri(copies, file.Name).AbsoluteUri } }
                : read;
        }
        catch (ModException ex)
        {
            Log.Warn($"The release of {offered.Repo} could not be read: {ex.Message}");
        }
        isReleaseRead = true;
    }

    [RelayCommand]
    private void ShowNotes() => owner.RecommendedNotes = this;

    [RelayCommand]
    private void CloseNotes() => owner.RecommendedNotes = null;

    [RelayCommand]
    private void ShowThanks() => owner.RecommendedThanks = this;

    [RelayCommand]
    private void CloseThanks() => owner.RecommendedThanks = null;

    [RelayCommand]
    private void OpenThanksLink(string url) => owner.OpenPath(url);

    [RelayCommand]
    private void OpenSource()
    {
        if (Source is { } source)
            owner.OpenPath($"https://{source}");
    }

    /// <summary>Installs or updates the mod, as any download from a release. A mod on UE4SS gets a missing or older UE4SS first, from the same click.</summary>
    [RelayCommand]
    private async Task Install()
    {
        if (mod is not { } offered || release is not { File: { } file } newest || !owner.EnsureGame())
            return;
        if (NeedsUe4ssFirst)
        {
            await owner.DownloadAndInstallUe4ssAsync();
            if (NeedsUe4ssFirst)
                return;
        }
        await owner.InstallFromReleaseAsync(offered.Repo, Item?.Record.Name ?? offered.InstalledName ?? offered.Name, newest.Version, file.Urls, file.Name, file.Size, file.Sha256);
    }

    /// <summary>Removes the extra mod, with the question every uninstall asks.</summary>
    [RelayCommand]
    private Task Retire() => Item is { } item ? item.UninstallCommand.ExecuteAsync(null) : Task.CompletedTask;
}
