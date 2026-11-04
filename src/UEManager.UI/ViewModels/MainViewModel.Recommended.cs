using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UEManager.Core;
using UEManager.Core.Mods;
using UEManager.Core.Updates;

namespace UEManager.UI.ViewModels;

/// <summary>
/// The mod the app recommends for this game, at the top of the Mods page and apart from the player's own mods. The
/// manifest names it and its GitHub repo, so it can come and go without a new app version. Its newest release is read
/// at every start, as all remote data: first the copy next to the manifest, then GitHub.
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>What the manifest says; null when it names no mod, or has not answered.</summary>
    private RecommendedMod? recommended;

    /// <summary>The manifest answered at this start, so its word on the recommended mod counts. Offline, the marks stay.</summary>
    private bool isRecommendedKnown;

    /// <summary>The newest release of the mod's repo; null while it is read, or when it could not be read.</summary>
    private AppRelease? recommendedRelease;

    private bool isRecommendedReleaseRead;

    [ObservableProperty]
    private bool isRecommendedNotesOpen;

    /// <summary>The installed mod, with the row every mod has.</summary>
    public ModItemViewModel? RecommendedItem { get; private set; }

    private ModRecord? RecommendedRecord => RecommendedItem is null ? null : manager?.Mods.FirstOrDefault(m => m.Recommended);

    public bool HasRecommended => recommended is not null || RecommendedItem is not null;

    public bool IsRecommendedInstalled => RecommendedItem is not null;

    /// <summary>Not installed yet: the row, with the mod's name, is not there.</summary>
    public bool ShowRecommendedName => RecommendedItem is null && recommended is not null;

    /// <summary>The card's label: an experimental mod says so, from the manifest.</summary>
    public string RecommendedLabel => recommended?.Experimental == true ? L.T("Recommended - Experimental") : L.T("Recommended");

    public string? RecommendedName => recommended?.Name;

    public string? RecommendedText => recommended?.TextIn(L.Current.Language);

    public bool HasRecommendedText => !string.IsNullOrWhiteSpace(RecommendedText);

    /// <summary>The technical notes in the chosen language, as paragraphs; the manifest may have none.</summary>
    public IReadOnlyList<string> RecommendedNotes =>
        recommended?.NotesIn(L.Current.Language)?.Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];

    public bool HasRecommendedNotes => RecommendedNotes.Count > 0;

    /// <summary>Where the mod's source code is, as the notes show it.</summary>
    public string? RecommendedSource => recommended is { } mod ? $"github.com/{mod.Repo}" : null;

    /// <summary>A click also installs or updates UE4SS first: such a mod may work only with the UE4SS the manifest names.</summary>
    private bool NeedsUe4ssFirst => AvailableUe4ss is not null && (!IsUe4ssInstalled || IsUe4ssUpdateAvailable);

    /// <summary>The installed mod has a newer version: the card offers it, and the Mods entry in the sidebar says so.</summary>
    public bool IsRecommendedUpdateAvailable =>
        recommended is not null && recommendedRelease is { File: not null } release
        && RecommendedRecord is { } installed && ModVersion.IsUpdate(installed.Version, release.Version);

    /// <summary>The button: install or update, with UE4SS when it needs it; none when there is nothing to do or no file to get.</summary>
    public string? RecommendedActionText
    {
        get
        {
            if (recommended is null || recommendedRelease is not { File: not null } release)
                return null;
            if (RecommendedRecord is not null)
            {
                if (!IsRecommendedUpdateAvailable)
                    return null;
                return NeedsUe4ssFirst ? L.T("Update UE4SS and this mod") : L.F($"Update to {release.Version}");
            }
            return !NeedsUe4ssFirst ? L.T("Download and install")
                : IsUe4ssInstalled ? L.T("Update UE4SS and install this mod")
                : L.T("Install UE4SS and this mod");
        }
    }

    public bool HasRecommendedAction => RecommendedActionText is not null;

    /// <summary>A short line under the mod's text: its newest version, or why there is no button.</summary>
    public string? RecommendedStatusText
    {
        get
        {
            if (recommended is null)
                return null;
            if (!isRecommendedReleaseRead)
                return L.T("Looking for the newest version…");
            if (recommendedRelease is not { File: not null } release)
                return L.T("The download cannot be reached right now. The app tries again at the next start.");
            return RecommendedRecord is null ? L.F($"Version {release.Version}") : null;
        }
    }

    public bool HasRecommendedStatus => RecommendedStatusText is not null;

    /// <summary>What the manifest said at this start: marks the mod, and reads the newest release of its repo.</summary>
    private async Task ReadRecommendedAsync(Ue4ssManifestAnswer answer)
    {
        recommended = answer.Recommended;
        isRecommendedKnown = true;
        recommendedRelease = null;
        isRecommendedReleaseRead = false;
        RefreshMods();
        if (recommended is not { } mod)
            return;
        // The copy of the mod's release, next to the manifest: on the host that just answered.
        var copies = new Uri(new Uri(answer.Url), $"mods/{mod.RepoName}/");
        try
        {
            var release = await AppReleases.FetchLatestAsync(Http,
                [new Uri(copies, "latest.json").AbsoluteUri, $"https://api.github.com/repos/{mod.Repo}/releases/latest"], mod.FilePrefix);
            // GitHub's answer names only its own file: the copy is the way on when that download fails.
            recommendedRelease = release.File is { FallbackUrl: null } file
                ? release with { File = file with { FallbackUrl = new Uri(copies, file.Name).AbsoluteUri } }
                : release;
        }
        catch (ModException ex)
        {
            Log.Warn($"The recommended mod's release could not be read: {ex.Message}");
        }
        isRecommendedReleaseRead = true;
        OnPropertyChanged(string.Empty);
    }

    [RelayCommand]
    private void ShowRecommendedNotes() => IsRecommendedNotesOpen = true;

    [RelayCommand]
    private void CloseRecommendedNotes() => IsRecommendedNotesOpen = false;

    [RelayCommand]
    private void OpenRecommendedSource()
    {
        if (RecommendedSource is { } source)
            OpenPath($"https://{source}");
    }

    /// <summary>
    /// Installs or updates the recommended mod, as any download from a release. It runs on UE4SS, so a missing or
    /// older UE4SS comes first, from the same click.
    /// </summary>
    [RelayCommand]
    private async Task InstallRecommended()
    {
        if (recommended is not { } mod || recommendedRelease is not { File: { } file } release || !EnsureGame())
            return;
        if (NeedsUe4ssFirst)
        {
            await DownloadAndInstallUe4ssAsync();
            if (NeedsUe4ssFirst)
                return;
        }
        await InstallFromReleaseAsync(mod.Repo, RecommendedRecord?.Name ?? mod.Name, release.Version, file.Urls, file.Name, file.Size, file.Sha256);
    }
}
