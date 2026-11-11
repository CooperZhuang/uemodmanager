using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ICSharpCode.AvalonEdit.Document;
using Microsoft.Win32;
using UEManager.Core;
using UEManager.Core.Games;
using UEManager.UI.Services;

namespace UEManager.UI.ViewModels;

/// <summary>
/// The scan of a game's folder, for a game the app does not know yet: the player chooses the folder, and the app shows
/// the folder report (see <see cref="FolderReport"/>) in a view of its own. From there the player copies it, saves it as a
/// text file, or takes it to our Discord. Nothing is copied or saved without a click.
/// </summary>
public sealed partial class FolderScanViewModel : LocalizedObservableObject
{
    /// <summary>Asks for the game's folder; null when the player cancels. Tests put their own in its place.</summary>
    internal Func<string?> PickFolder { get; set; } = () =>
    {
        var dialog = new OpenFolderDialog { Title = L.T("Choose the folder your game is installed in") };
        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    };

    /// <summary>Asks where to save the report, starting from a file name; null when the player cancels. Tests put their own in its place.</summary>
    internal Func<string, string?> PickSaveFile { get; set; } = name =>
    {
        var dialog = new SaveFileDialog { Title = L.T("Save the folder report"), FileName = name, DefaultExt = ".txt", Filter = "*.txt|*.txt" };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    };

    /// <summary>Puts text on the clipboard. Tests put their own in its place.</summary>
    internal Action<string> CopyText { get; set; } = Clipboard.SetText;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand))]
    private bool isScanning;

    /// <summary>The line under the scan button: the scan at work, or why it failed; null otherwise.</summary>
    [ObservableProperty]
    private string? statusText;

    /// <summary>The report of the last scan; null while its view is closed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReportOpen))]
    private string? report;

    /// <summary>The report for the text view.</summary>
    [ObservableProperty]
    private TextDocument? document;

    /// <summary>The name of the folder the report is of.</summary>
    [ObservableProperty]
    private string? reportTitle;

    /// <summary>A note for a folder that does not look like an Unreal game; null for one that does.</summary>
    [ObservableProperty]
    private string? reportWarning;

    /// <summary>What the last click in the report's view did, such as "Copied."; null before one.</summary>
    [ObservableProperty]
    private string? feedback;

    [ObservableProperty]
    private bool isFeedbackWarning;

    /// <summary>The report's view shows in place of the picker.</summary>
    public bool IsReportOpen => Report is not null;

    private bool CanScan() => !IsScanning;

    [RelayCommand(CanExecute = nameof(CanScan))]
    private async Task Scan()
    {
        if (PickFolder() is not { } folder)
            return;
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(folder)) is { Length: > 0 } last ? last : folder;
        IsScanning = true;
        StatusText = L.T("Scanning the folder…");
        try
        {
            var result = await Task.Run(() => FolderReport.Build(folder, $"{AppInfo.Name} {AppInfo.Version}", DateTime.Now));
            Log.Info($"Folder report of '{name}': {result.Text.Length} characters.");
            StatusText = null;
            ReportTitle = name;
            ReportWarning = result.LooksLikeUnreal
                ? null
                : L.T("This folder does not look like an Unreal Engine game: it has no Content\\Paks folder. Check that you chose the game's main folder.");
            Feedback = null;
            Document = new TextDocument(result.Text);
            Report = result.Text;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            Log.Warn($"The folder '{folder}' could not be scanned: {ex.Message}");
            StatusText = L.F($"The folder could not be scanned: {ex.Message}");
        }
        finally
        {
            IsScanning = false;
        }
    }

    [RelayCommand]
    private void CopyReport()
    {
        if (Report is not { } text)
            return;
        try
        {
            CopyText(text);
            Tell(L.T("Copied. Paste it into our Discord."));
        }
        catch (COMException ex)
        {
            Tell(L.F($"The clipboard is in use by another app. Try again. ({ex.Message})"), warning: true);
        }
    }

    [RelayCommand]
    private void SaveReport()
    {
        if (Report is not { } text)
            return;
        var safe = string.Concat((ReportTitle ?? "game").Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        if (PickSaveFile($"folder-report-{safe}.txt") is not { } file)
            return;
        try
        {
            File.WriteAllText(file, text);
            Tell(L.F($"Saved: {file}"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"The folder report could not be saved to '{file}': {ex.Message}");
            Tell(L.F($"The report could not be saved: {ex.Message}"), warning: true);
        }
    }

    /// <summary>The report's view invites the player to our Discord as well, where the report goes.</summary>
    [RelayCommand]
    private void OpenDiscord()
    {
        try
        {
            Shell.Open(AppInfo.DiscordUrl);
        }
        catch (Exception ex) when (ex is Win32Exception or IOException)
        {
            Log.Warn($"Discord could not be opened: {ex.Message}");
        }
    }

    /// <summary>Back to the picker; the report is gone unless the player copied or saved it.</summary>
    [RelayCommand]
    private void CloseReport()
    {
        Report = null;
        Document = null;
        ReportWarning = null;
        Feedback = null;
    }

    private void Tell(string text, bool warning = false)
    {
        Feedback = text;
        IsFeedbackWarning = warning;
    }
}
