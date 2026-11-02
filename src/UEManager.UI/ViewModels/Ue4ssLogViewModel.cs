using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UEManager.Core;
using UEManager.Core.Mods;
using UEManager.UI.Services;

namespace UEManager.UI.ViewModels;

/// <summary>
/// A log line with the colour of its level: "bad" for an error, "warn" for a warning, "dim" for a known line, which
/// UE4SS writes at every start and which does no harm.
/// </summary>
public sealed record Ue4ssLogLineItem(string Text, string Tone, bool Known);

/// <summary>The errors and warnings in a tab of the log, for the count next to its name; it shows when there are any.</summary>
public sealed record Ue4ssLogTabCount(string Text, string Tone, bool Shows);

/// <summary>
/// The log part of the UE4SS page: the end of UE4SS.log from the last game start, errors and warnings marked, in three
/// tabs: the lines of UE4SS itself, of the player's mods, and of scripts that do not say which mod they are.
/// </summary>
public sealed partial class Ue4ssLogViewModel(MainViewModel owner) : LocalizedObservableObject
{
    private Ue4ssRuntime? runtime;
    private Ue4ssLog? log;
    private IReadOnlyList<string> errorMods = [];
    private string? readError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShownLines), nameof(FoundText), nameof(IsTabEmpty))]
    private bool onlyProblems;

    /// <summary>Text the shown lines contain, such as a mod's name; it stays when the log is read again.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShownLines), nameof(IsSearching), nameof(FoundText), nameof(IsTabEmpty))]
    private string searchText = "";

    /// <summary>The tab shown; it stays when the log is read again.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShownLines), nameof(FoundText), nameof(IsTabEmpty), nameof(IsUe4ssTab), nameof(IsModsTab), nameof(IsUnnamedTab))]
    private Ue4ssLogSource tab;

    public bool IsUe4ssTab
    {
        get => Tab == Ue4ssLogSource.Ue4ss;
        set { if (value) Tab = Ue4ssLogSource.Ue4ss; }
    }

    public bool IsModsTab
    {
        get => Tab == Ue4ssLogSource.Mod;
        set { if (value) Tab = Ue4ssLogSource.Mod; }
    }

    public bool IsUnnamedTab
    {
        get => Tab == Ue4ssLogSource.Unnamed;
        set { if (value) Tab = Ue4ssLogSource.Unnamed; }
    }

    public Ue4ssLogTabCount Ue4ssCount => CountIn(Ue4ssLogSource.Ue4ss);

    public Ue4ssLogTabCount ModsCount => CountIn(Ue4ssLogSource.Mod);

    public Ue4ssLogTabCount UnnamedCount => CountIn(Ue4ssLogSource.Unnamed);

    private Ue4ssLogTabCount CountIn(Ue4ssLogSource source)
    {
        var problems = log?.Lines.Where(l => l.Source == source && l.IsProblem).ToList() ?? [];
        return new Ue4ssLogTabCount(problems.Count.ToString(CultureInfo.CurrentCulture),
            problems.Any(l => l.Level == Ue4ssLogLevel.Error) ? "bad" : "warn", problems.Count > 0);
    }

    public bool HasLog => log is not null;

    /// <summary>The log has lines, but none in this tab for the checkbox and the search.</summary>
    public bool IsTabEmpty => HasLog && ShownLines.Count == 0;

    public bool IsSearching => !string.IsNullOrWhiteSpace(SearchText);

    public string FoundText => L.F($"Lines found: {ShownLines.Count}");

    public IReadOnlyList<Ue4ssLogLineItem> ShownLines => log is null
        ? []
        : log.Lines
            .Where(l => l.Source == Tab && Shows(l, OnlyProblems, SearchText))
            .Select(l => new Ue4ssLogLineItem(l.Text, l.Known ? "dim" : l.Level switch
            {
                Ue4ssLogLevel.Error => "bad",
                Ue4ssLogLevel.Warning => "warn",
                _ => "",
            }, l.Known))
            .ToList();

    /// <summary>
    /// Whether a line shows: an error or warning that is not a known line, when only those are wanted, and containing the
    /// search text in any case. A line of a mod also shows for its folder name, as a Lua mod's tag can differ from it.
    /// </summary>
    internal static bool Shows(Ue4ssLogLine line, bool onlyProblems, string search) =>
        (!onlyProblems || line.IsProblem)
        && (string.IsNullOrWhiteSpace(search)
            || line.Text.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase)
            || line.Mod?.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase) == true);

    public string SummaryText => (runtime, log) switch
    {
        ({ Installed: false } or null, _) => L.T("UE4SS is not installed, so there is no log."),
        _ when readError is not null => L.F($"The log could not be read: {readError}"),
        (_, null) => L.T("No log yet. UE4SS writes it each time the game starts."),
        _ => L.F($"Game start of {log.WrittenAt:g}:  {Errors(log)},  {Warnings(log)}.") + Known(log) + Shortened(log),
    };

    private static string Errors(Ue4ssLog log) => L.Plural(log.ErrorCount, "{0} error", "{0} errors");

    private static string Warnings(Ue4ssLog log) => L.Plural(log.WarningCount, "{0} warning", "{0} warnings");

    private static string Known(Ue4ssLog log) =>
        log.KnownCount > 0 ? "  " + L.Plural(log.KnownCount, "{0} known line does no harm.", "{0} known lines do no harm.") : "";

    private static string Shortened(Ue4ssLog log) => log.Truncated ? "  " + L.F($"Showing the last {Ue4ssLog.MaxLines} lines.") : "";

    public string SummaryTone => log is null ? "" : log.ErrorCount > 0 ? "bad" : log.WarningCount > 0 ? "warn" : "good";

    public bool HasErrorMods => errorMods.Count > 0;

    public string ErrorModsText => L.F($"Errors name these mods: {string.Join(", ", errorMods)}");

    /// <summary>Reads the log of the installed UE4SS again.</summary>
    public async Task LoadAsync(Ue4ssRuntime? current)
    {
        runtime = current;
        log = null;
        errorMods = [];
        readError = null;
        if (current is { Installed: true })
        {
            try
            {
                (log, errorMods) = await Task.Run(() =>
                {
                    var folders = current.ModFolders();
                    var read = Ue4ssLog.Read(current.LogPath, folders);
                    return (read, read?.ModsNamedInErrors(folders) ?? []);
                });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Warn($"UE4SS.log could not be read: {ex.Message}");
                readError = ex.Message;
            }
        }
        OnPropertyChanged(string.Empty);
    }

    [RelayCommand]
    private Task Refresh() => owner.RefreshUe4ssLogAsync();

    /// <summary>Deletes the log, so its old lines are gone; UE4SS writes a new one at the next game start.</summary>
    [RelayCommand]
    private Task Clear() => owner.ClearUe4ssLogAsync();

    [RelayCommand]
    private void OpenLog()
    {
        if (log is not null)
            owner.OpenPath(log.Path);
    }

    [RelayCommand]
    private void Copy()
    {
        if (log is null)
            return;
        try
        {
            Clipboard.SetText(string.Join(Environment.NewLine, log.Lines.Select(l => l.Text)));
            owner.ShowToast(L.T("The UE4SS log is on the clipboard."));
        }
        catch (COMException ex)
        {
            owner.ShowToast(L.F($"The clipboard is in use by another app: {ex.Message}"), isError: true);
        }
    }
}
