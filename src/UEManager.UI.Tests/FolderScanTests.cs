using System.IO;
using System.Runtime.InteropServices;
using UEManager.UI.Services;
using UEManager.UI.ViewModels;
using Xunit;

namespace UEManager.UI.Tests;

/// <summary>The scan of a game folder in the game picker, for a game the app does not know yet, and its report's view.</summary>
public sealed class FolderScanTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "uemm-scan-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(folder))
            Directory.Delete(folder, recursive: true);
    }

    private string Game(string name, bool unreal = true)
    {
        var game = Path.Combine(folder, name);
        var file = Path.Combine(game, unreal ? @"Game\Content\Paks\Game-Windows.pak" : "readme.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, "x");
        return game;
    }

    [Fact]
    public async Task A_scan_opens_the_report_and_copies_or_saves_nothing_by_itself()
    {
        var game = Game("New Game");
        var copied = new List<string>();
        var saves = 0;
        var scan = new FolderScanViewModel
        {
            PickFolder = () => game,
            CopyText = copied.Add,
            PickSaveFile = _ =>
            {
                saves++;
                return null;
            },
        };

        await scan.ScanCommand.ExecuteAsync(null);

        Assert.True(scan.IsReportOpen);
        Assert.Equal("New Game", scan.ReportTitle);
        Assert.StartsWith("UE MM folder report", scan.Report);
        // The text view's document; it belongs to the thread that made it, the window's in the app.
        Assert.NotNull(scan.Document);
        Assert.Null(scan.ReportWarning);
        Assert.Null(scan.StatusText);
        Assert.Null(scan.Feedback);
        Assert.Empty(copied);
        Assert.Equal(0, saves);
        Assert.False(scan.IsScanning);
    }

    [Fact]
    public async Task Copy_and_save_work_from_the_report_and_Close_goes_back()
    {
        var game = Game("New Game");
        var copied = new List<string>();
        string? offered = null;
        var target = Path.Combine(folder, "out", "report.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var scan = new FolderScanViewModel
        {
            PickFolder = () => game,
            CopyText = copied.Add,
            PickSaveFile = name =>
            {
                offered = name;
                return target;
            },
        };
        await scan.ScanCommand.ExecuteAsync(null);

        scan.CopyReportCommand.Execute(null);
        Assert.Equal(scan.Report, Assert.Single(copied));
        Assert.Equal("Copied. Paste it into our Discord.", scan.Feedback);

        scan.SaveReportCommand.Execute(null);
        Assert.Equal("folder-report-New Game.txt", offered);
        Assert.Equal(scan.Report, File.ReadAllText(target));
        Assert.Equal($"Saved: {target}", scan.Feedback);
        Assert.False(scan.IsFeedbackWarning);

        scan.CloseReportCommand.Execute(null);
        Assert.False(scan.IsReportOpen);
        Assert.Null(scan.Document);
        Assert.Null(scan.Feedback);
    }

    [Fact]
    public async Task Without_a_folder_nothing_happens()
    {
        var scan = new FolderScanViewModel { PickFolder = () => null, CopyText = _ => throw new InvalidOperationException() };

        await scan.ScanCommand.ExecuteAsync(null);

        Assert.False(scan.IsReportOpen);
        Assert.Null(scan.StatusText);
    }

    [Fact]
    public async Task A_folder_without_an_unreal_game_shows_its_report_with_a_warning()
    {
        var other = Game("Photos", unreal: false);
        var scan = new FolderScanViewModel { PickFolder = () => other };

        await scan.ScanCommand.ExecuteAsync(null);

        Assert.True(scan.IsReportOpen);
        Assert.Contains("does not look like an Unreal Engine game", scan.ReportWarning);
    }

    [Fact]
    public async Task A_busy_clipboard_says_so_and_the_report_stays()
    {
        var game = Game("New Game");
        var scan = new FolderScanViewModel { PickFolder = () => game, CopyText = _ => throw new COMException("OpenClipboard failed") };
        await scan.ScanCommand.ExecuteAsync(null);

        scan.CopyReportCommand.Execute(null);

        Assert.True(scan.IsFeedbackWarning);
        Assert.StartsWith("The clipboard is in use by another app.", scan.Feedback);
        Assert.True(scan.IsReportOpen);
    }

    [Fact]
    public void Only_one_panel_of_the_picker_is_open_at_a_time()
    {
        var picker = new GamePickerViewModel([], null, _ => null, _ => { }, null) { IsMessageOpen = true };

        picker.IsMissingGameOpen = true;
        Assert.False(picker.IsMessageOpen);
        picker.IsMessageOpen = true;
        Assert.False(picker.IsMissingGameOpen);
        Assert.False(picker.Scan.IsReportOpen);
    }

    [Fact]
    public void The_report_has_its_own_colours_from_the_look() => UiThread.Run(() =>
    {
        var red = System.Windows.Media.Colors.Red;
        var report = Syntax.Build("Report", key => key == "Color.Syntax.Key" ? red : null);

        Assert.Equal(red, report.GetNamedColor("Key").Foreground.GetColor(null));
        // Its headings are drawn as keys, as the sections of a settings file.
        Assert.Equal(red, report.GetNamedColor("Section").Foreground.GetColor(null));
        Assert.NotNull(report.GetNamedColor("Comment"));
    });

    [Theory]
    [InlineData("UE MM folder report", "UE MM folder report=Section")]
    [InlineData("Summary", "Summary=Section")]
    [InlineData("  Unreal project folder: Game (Content\\Paks: 1 .pak)", "Unreal project folder=Key")]
    [InlineData("    ~mods: yes, 3 file(s)", "~mods=Key")]
    [InlineData("Folder: %USERPROFILE%\\Games\\X", "Folder=Key|%USERPROFILE%=Comment")]
    [InlineData("      CustomShellSystem/  (+5 other files)", "CustomShellSystem/=String|(+5 other files)=Comment")]
    [InlineData("        css_core-1.0.10.dll  4.7 MB", "4.7 MB=Value")]
    [InlineData("  My Game/", "My Game/=String")]
    [InlineData("        <steam id>/", "<steam id>=Comment")]
    public void The_report_colours_its_parts(string line, string expected) => UiThread.Run(() =>
    {
        var document = new ICSharpCode.AvalonEdit.Document.TextDocument(line);
        var highlighter = new ICSharpCode.AvalonEdit.Highlighting.DocumentHighlighter(document, Syntax.Build("Report", _ => null));

        var parts = highlighter.HighlightLine(1).Sections
            .Select(section => $"{line.Substring(section.Offset, section.Length)}={section.Color.Name}");

        Assert.Equal(expected, string.Join("|", parts));
    });
}
