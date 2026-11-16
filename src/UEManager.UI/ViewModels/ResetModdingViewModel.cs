using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ICSharpCode.AvalonEdit.Document;
using Microsoft.Win32;
using UEManager.Core;
using UEManager.Core.Mods;
using UEManager.UI.Services;

namespace UEManager.UI.ViewModels;

/// <summary>Where "Reset modding" is: the question before it, the work, or its end.</summary>
public enum ResetStage
{
    Asking,
    Working,
    Done,
    Failed,
}

/// <summary>
/// "Reset modding" in a view of its own, over the whole window below the title bar: at the top the question, what it
/// changes in the game and two choices that go further; below, the log of the work as it happens, with every file and
/// folder it touches, so a file in use is named. The view stays open at the end, until the player closes it.
/// </summary>
public sealed partial class ResetModdingViewModel(string gameName, ResetPreview preview) : LocalizedObservableObject
{
    private readonly TaskCompletionSource<bool> result = new();
    private readonly TaskCompletionSource closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly SynchronizationContext? window = SynchronizationContext.Current;
    private readonly ConcurrentQueue<LogLine> pending = new();
    private int flushPosted;

    /// <summary>Asks where to save the log, starting from a file name; null when the player cancels. Tests put their own in its place.</summary>
    internal Func<string, string?> PickSaveFile { get; set; } = name =>
    {
        var dialog = new SaveFileDialog { Title = L.T("Save the reset log"), FileName = name, DefaultExt = ".txt", Filter = "*.txt|*.txt" };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    };

    /// <summary>Puts text on the clipboard. Tests put their own in its place.</summary>
    internal Action<string> CopyText { get; set; } = Clipboard.SetText;

    /// <summary>The mods also leave the manager, as when each is uninstalled.</summary>
    [ObservableProperty]
    private bool deleteMods;

    /// <summary>The app's own settings go back to their defaults; the app then starts again.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CloseText))]
    private bool resetSettings;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAsking), nameof(IsWorking), nameof(IsFinished), nameof(HasFailed), nameof(CloseText))]
    private ResetStage stage;

    /// <summary>The line under the buttons: the work going on, what it did, or why it stopped; null while the view asks.</summary>
    [ObservableProperty]
    private string? statusText;

    /// <summary>What the last click on Copy or Save did, such as "Copied."; null before one.</summary>
    [ObservableProperty]
    private string? feedback;

    [ObservableProperty]
    private bool isFeedbackWarning;

    public bool IsAsking => Stage == ResetStage.Asking;

    public bool IsWorking => Stage == ResetStage.Working;

    public bool IsFinished => Stage is ResetStage.Done or ResetStage.Failed;

    public bool HasFailed => Stage == ResetStage.Failed;

    /// <summary>After a reset that also resets the app's settings, closing the view starts the app again.</summary>
    public string CloseText => Stage == ResetStage.Done && ResetSettings ? L.T("Start again") : L.T("Close");

    public string Intro => L.F($"This takes {gameName} back to its original files, so you can start clean. Close the game first.");

    /// <summary>What the reset changes in the game, one line each.</summary>
    public IReadOnlyList<string> Changes { get; } = DescribeChanges(preview);

    public bool HasMods => preview.InstalledMods > 0;

    public string DeleteModsText => HasMods
        ? L.Join(L.Plural(preview.InstalledMods, "Also delete the {0} mod from the manager.", "Also delete the {0} mods from the manager."),
            L.T("Without this, they stay in the Mods list, turned off, ready to turn on again."))
        : L.T("Also delete the mods from the manager. There are none.");

    /// <summary>The log of the work, a line for each step; empty until the reset starts.</summary>
    public TextDocument Document { get; } = new();

    public bool HasLog => Document.TextLength > 0;

    /// <summary>The player's answer to the question: true to reset.</summary>
    public Task<bool> Result => result.Task;

    /// <summary>The player closed the view after the work.</summary>
    public Task Closed => closed.Task;

    [RelayCommand]
    private void Accept() => result.TrySetResult(true);

    [RelayCommand]
    private void Cancel() => result.TrySetResult(false);

    [RelayCommand]
    private void Close() => closed.TrySetResult();

    private Action? keepVersion;

    /// <summary>The reset stopped at files changed outside the manager: the view offers to keep the player's version.</summary>
    public bool CanKeepVersion => keepVersion is not null;

    internal void OfferKeepVersion(Action keep)
    {
        keepVersion = keep;
        OnPropertyChanged(nameof(CanKeepVersion));
    }

    /// <summary>Closes the view and keeps the player's version of the changed files, as the message of any other change offers.</summary>
    [RelayCommand]
    private void KeepVersion()
    {
        Close();
        keepVersion?.Invoke();
    }

    /// <summary>The reset begins: the choices are fixed, and the log fills.</summary>
    internal void Start()
    {
        Stage = ResetStage.Working;
        StatusText = L.T("Resetting modding…");
    }

    /// <summary>A line of the work's log; from any thread. The view shows it at the next turn of the window's thread.</summary>
    internal void Add(LogLine line)
    {
        pending.Enqueue(line);
        if (Interlocked.Exchange(ref flushPosted, 1) == 1)
            return;
        if (window is null)
            Flush();
        else
            window.Post(_ => Flush(), null);
    }

    /// <summary>Moves the waiting lines into the log in one piece, so a thousand files do not mean a thousand redraws.</summary>
    private void Flush()
    {
        Interlocked.Exchange(ref flushPosted, 0);
        var text = new StringBuilder();
        while (pending.TryDequeue(out var line))
            text.Append(line).Append('\n');
        if (text.Length == 0)
            return;
        Document.Insert(Document.TextLength, text.ToString());
        OnPropertyChanged(nameof(HasLog));
    }

    /// <summary>The work ended: what it did, or why it stopped. The view stays open until the player closes it.</summary>
    internal void Finish(string text, bool failed)
    {
        Flush();
        StatusText = text;
        Stage = failed ? ResetStage.Failed : ResetStage.Done;
    }

    [RelayCommand]
    private void CopyLog()
    {
        try
        {
            CopyText(Document.Text);
            Tell(L.T("Copied. Paste it into our Discord."));
        }
        catch (COMException ex)
        {
            Tell(L.F($"The clipboard is in use by another app. Try again. ({ex.Message})"), warning: true);
        }
    }

    [RelayCommand]
    private void SaveLog()
    {
        if (PickSaveFile($"reset-log-{DateTime.Now:yyyyMMdd-HHmmss}.txt") is not { } file)
            return;
        try
        {
            File.WriteAllText(file, Document.Text);
            Tell(L.F($"Saved: {file}"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"The reset log could not be saved to '{file}': {ex.Message}");
            Tell(L.F($"The log could not be saved: {ex.Message}"), warning: true);
        }
    }

    private void Tell(string text, bool warning = false)
    {
        Feedback = text;
        IsFeedbackWarning = warning;
    }

    private static List<string> DescribeChanges(ResetPreview preview)
    {
        var changes = new List<string>();
        if (preview.EnabledMods > 0)
            changes.Add(L.Plural(preview.EnabledMods, "{0} enabled mod is removed from the game.", "{0} enabled mods are removed from the game."));
        if (preview.Ue4ssInstalled)
            changes.Add(L.T("UE4SS is removed, with its settings and every mod in its folder, also mods put there by hand."));
        if (preview.OtherFiles > 0)
            changes.Add(L.Plural(preview.OtherFiles,
                "{0} file in the mods folders that the manager did not install moves to a backup folder.",
                "{0} files in the mods folders that the manager did not install move to a backup folder."));
        if (changes.Count == 0)
            changes.Add(L.T("The game has no mods and no UE4SS to remove."));
        return changes;
    }
}
