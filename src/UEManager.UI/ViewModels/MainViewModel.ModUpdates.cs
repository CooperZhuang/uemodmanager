using System.IO;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UEManager.Core;
using UEManager.Core.Mods;
using UEManager.Core.Updates;
using UEManager.UI.Services;

namespace UEManager.UI.ViewModels;

/// <summary>What the last update check found for a mod with a mod page: the version on the page, and whether it is newer.</summary>
/// <param name="IsAvailable">The page has a newer version than the installed one.</param>
/// <param name="Latest">The version on the mod page.</param>
/// <param name="Installed">The installed version it was compared with.</param>
public sealed record ModUpdateStatus(bool IsAvailable, string Latest, string Installed);

/// <summary>
/// The update check for mods with a mod page: one request for all of them, at each start (unless turned off in
/// Settings) and on the button in the mod list's header, which then waits 30 seconds. It only tells: the player
/// downloads a newer version on the mod page, and the app never downloads it.
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>How long the button waits after a check, so it cannot flood the mod site.</summary>
    internal static readonly TimeSpan ModUpdateWait = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan ModUpdateTimeout = TimeSpan.FromSeconds(15);

    /// <summary>The shortest time the button spins, however fast the mod site answers.</summary>
    private static readonly TimeSpan ModUpdateSpin = TimeSpan.FromSeconds(1);

    /// <summary>A check runs: the button in the mod list's header spins.</summary>
    public bool IsCheckingModUpdates => checkingModUpdates;

    /// <summary>
    /// The page version per mod number on the site, from the last check. A mod's state comes from its own version at
    /// the time, so an update in the manager shows at once, without asking the site again.
    /// </summary>
    private readonly Dictionary<string, string> pageVersions = new(StringComparer.Ordinal);

    private bool checkingModUpdates;
    /// <summary>When the last check started, on the system's running clock (<see cref="Environment.TickCount64"/>), which a change of the PC's clock does not move; null before the first.</summary>
    internal long? lastModUpdateCheck;
    private DispatcherTimer? modUpdateWait;

    /// <summary>The state of a mod for its update box; null when it was not checked, or the check could not tell.</summary>
    internal ModUpdateStatus? ModUpdateOf(ModRecord mod)
    {
        if (mod.NotTracked || ModNaming.SiteModId(mod.SourceFileName) is not { } id || !pageVersions.TryGetValue(id, out var page)
            || InstalledVersion(mod) is not { } installed)
            return null;
        return ModSiteVersions.Compare(installed, page) switch
        {
            ModSiteState.UpdateAvailable => new ModUpdateStatus(true, page, installed),
            ModSiteState.UpToDate => new ModUpdateStatus(false, page, installed),
            _ => null,
        };
    }

    /// <summary>
    /// The mod's version in the list, or else the one in its download's name: a mod installed before the app read
    /// that name, such as one whose name ends in "(1)", has no version in the list.
    /// </summary>
    private static string? InstalledVersion(ModRecord mod) =>
        mod.Version is { Length: > 0 } version ? version
        : mod.SourceFileName is { Length: > 0 } source ? ModNaming.FromSource(source).Version
        : null;

    /// <summary>The player keeps a mod's version, or tracks it again; see <see cref="ModRecord.NotTracked"/>.</summary>
    internal void SetModTracked(ModRecord mod, bool tracked)
    {
        if (manager is null)
            return;
        try
        {
            manager.SetTracked(mod.Id, tracked);
        }
        catch (Exception ex) when (ex is ModException or IOException or UnauthorizedAccessException)
        {
            ShowToast(L.F($"Something went wrong: {ex.Message}"), isError: true);
        }
        RefreshMods();
    }

    /// <summary>Puts in the page versions a check found, per mod number, and shows them in the rows.</summary>
    internal void ShowModUpdates(IReadOnlyDictionary<string, string> found)
    {
        pageVersions.Clear();
        foreach (var (id, version) in found)
            pageVersions[id] = version;
        RefreshMods();
    }

    /// <summary>A mod in the list has a newer version on its mod page.</summary>
    public bool HasModUpdates => Mods.Any(m => m.IsUpdateAvailable);

    /// <summary>The UPDATE mark on the Mods entry: a newer version of the card's mod or of a mod in the list.</summary>
    public bool IsAnyUpdateAvailable => IsRecommendedUpdateAvailable || HasModUpdates;

    /// <summary>The game profile names the mod site's API, so mods with a mod page can be checked.</summary>
    public bool HasModSite => Profile.ModSiteApiUrl is { Length: > 0 } && Profile.ModSiteGameId is { Length: > 0 };

    /// <summary>The notes on how the check works and where it cannot tell, from the box next to its button.</summary>
    [ObservableProperty]
    private bool isUpdateNotesOpen;

    /// <summary>For the curious: what is compared with what, the request, and the limits of a page's one version.</summary>
    public IReadOnlyList<string> UpdateNotes =>
    [
        L.T("What is compared: the mod's version in your list against the Version field of its mod page. Your list's version comes from the download's file name, such as 1.0.1 in \"Travel Anywhere 61 1.0.1 2026-10-29T13-42Z LbtEo64mS.zip\", or from Edit. The number after the name, 61, is the mod's ID on the site. Without a version in your list, the one in the file name counts."),
        L.T("The request: at each start and on the button, one request to the mod site's public API asks for the page version of every mod with a mod page, up to 50 per request. It sends only the game's ID and the mod IDs, needs no account and downloads nothing. The result stays until the next check, so a mod you update is compared again at once. The button then waits 30 seconds. Settings ▸ Mod updates turns off the check at start."),
        L.T("The rules: dotted numbers are compared number by number, so 1.10 is newer than 1.9 and 1.6 equals 1.6.0. Newer on the page shows Update available; the same or older shows Up to date. Versions with letters, such as 1.2-beta, count only when both are the same text. Otherwise the box stays empty, as after a failed check: the app log says why."),
        L.T("The limits: the API knows one version per mod page, the main version the author sets. It does not know which file you have, so the check is only as good as the author's version numbers:"),
        L.T("• A new file without a new page version shows no update."),
        L.T("• A file version that differs from the page, such as 10 against 1.2.4, cannot be matched."),
        L.T("• Several files on one page, such as styles or optional files, share one version."),
        L.T("• A renamed download has no mod ID, so no mod page and no check."),
        L.T("To keep a version, turn off Check this mod for updates in the mod's details: the box then says Not tracked, and the check leaves the mod out."),
        L.T("If a box looks wrong, compare the versions in its tooltip with the mod page, and fix yours with Edit."),
    ];

    [RelayCommand]
    private void ShowUpdateNotes() => IsUpdateNotesOpen = true;

    [RelayCommand]
    private void CloseUpdateNotes() => IsUpdateNotesOpen = false;

    /// <summary>How long the button still waits after the last check; zero or less when it may check.</summary>
    private TimeSpan ModUpdateWaitLeft => lastModUpdateCheck is { } started
        ? ModUpdateWait - TimeSpan.FromMilliseconds(Environment.TickCount64 - started)
        : TimeSpan.Zero;

    private bool IsWaitingAfterModUpdateCheck => ModUpdateWaitLeft > TimeSpan.Zero;

    public bool CanCheckModUpdates => HasModSite && !checkingModUpdates && !IsWaitingAfterModUpdateCheck;

    public string CheckModUpdatesHint =>
        checkingModUpdates ? L.T("Checking for updates…")
        : IsWaitingAfterModUpdateCheck ? L.T("You can check again 30 seconds after the last check.")
        : L.T("Check for updates");

    /// <summary>At each start the app checks, unless the player turned it off; the button works either way.</summary>
    public bool CheckModUpdatesAtStart
    {
        get => settings.CheckModUpdatesAtStart;
        set
        {
            settings.CheckModUpdatesAtStart = value;
            SaveSettings();
            OnPropertyChanged();
        }
    }

    [RelayCommand(CanExecute = nameof(CanCheckModUpdates))]
    private Task CheckModUpdates() => CheckModUpdatesAsync(byButton: true);

    /// <summary>
    /// Asks the mod site once for the page version of every mod with a mod page; the card's mods and mods without a mod
    /// page are left out, and with none of them nothing is sent. A failed check changes nothing in the rows; the log
    /// says why. After a press on the button, it waits 30 seconds; the check at start leaves it ready, so the first visit
    /// of the Mods page never finds it grey.
    /// </summary>
    internal async Task CheckModUpdatesAsync(bool byButton = false)
    {
        if (!CanCheckModUpdates || manager is not { } current)
            return;
        var ids = current.Mods
            .Where(m => !m.Recommended && !m.NotTracked && Profile.ModPage(m.SourceFileName) is not null)
            .Select(m => ModNaming.SiteModId(m.SourceFileName)!)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        BeginModUpdateCheck(byButton);
        try
        {
            // The button spins as long as the request takes, but at least a second, so a click is never without a sign;
            // the rows change when it stops.
            var shortest = Task.Delay(ModUpdateSpin);
            var asking = ids.Count == 0
                ? Task.FromResult<IReadOnlyDictionary<string, string>?>(null)
                : ModSiteVersions.FetchAsync(Http, Profile.ModSiteApiUrl!, Profile.ModSiteGameId!, ids, AppInfo.Version, ModUpdateTimeout);
            await Task.WhenAll(shortest, asking);
            if (await asking is { } found)
                ShowModUpdates(found);
        }
        finally
        {
            EndModUpdateCheck();
        }
    }

    /// <summary>A check starts: the button spins, and after a press it then waits.</summary>
    internal void BeginModUpdateCheck(bool byButton)
    {
        checkingModUpdates = true;
        if (byButton)
        {
            lastModUpdateCheck = Environment.TickCount64;
            StartModUpdateWait();
        }
        OnModUpdateCheckChanged();
    }

    internal void EndModUpdateCheck()
    {
        checkingModUpdates = false;
        OnModUpdateCheckChanged();
    }

    /// <summary>The button comes back when the wait is over.</summary>
    private void StartModUpdateWait()
    {
        if (modUpdateWait is null)
        {
            modUpdateWait = new DispatcherTimer();
            modUpdateWait.Tick += (_, _) => OnModUpdateWaitTick();
        }
        modUpdateWait.Stop();
        modUpdateWait.Interval = ModUpdateWait;
        modUpdateWait.Start();
    }

    /// <summary>
    /// The timer may fire a little early: then it waits for the rest, or the button would stay grey, as nothing else
    /// brings it back.
    /// </summary>
    internal void OnModUpdateWaitTick()
    {
        var left = ModUpdateWaitLeft;
        if (left > TimeSpan.Zero && modUpdateWait is not null)
        {
            modUpdateWait.Stop();
            modUpdateWait.Interval = left + TimeSpan.FromMilliseconds(50);
            modUpdateWait.Start();
            return;
        }
        modUpdateWait?.Stop();
        OnModUpdateCheckChanged();
    }

    private void OnModUpdateCheckChanged()
    {
        OnPropertyChanged(nameof(IsCheckingModUpdates));
        OnPropertyChanged(nameof(CanCheckModUpdates));
        OnPropertyChanged(nameof(CheckModUpdatesHint));
        CheckModUpdatesCommand.NotifyCanExecuteChanged();
    }
}
