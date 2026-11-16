using System.IO;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Highlighting;
using UEManager.Core;
using UEManager.Core.Mods;
using UEManager.UI.Services;
using UEManager.UI.ViewModels;
using Xunit;

namespace UEManager.UI.Tests;

/// <summary>"Reset modding" in its own view: the question, then the log of the work, which stays until Close.</summary>
public sealed class ResetModdingTests
{
    private static ResetModdingViewModel View() => new("Phantom Blade Zero", new ResetPreview(2, 3, true, 1));

    private static LogLine Line(string message, string level = "INFO") => new(new DateTime(2026, 11, 16, 14, 5, 9), level, message);

    /// <summary>Runs what is posted to it only when asked, as a window's thread does at its next turn.</summary>
    private sealed class LaterContext : SynchronizationContext
    {
        private readonly Queue<(SendOrPostCallback Callback, object? State)> posted = new();

        public override void Post(SendOrPostCallback d, object? state) => posted.Enqueue((d, state));

        public int Count => posted.Count;

        public void RunAll()
        {
            while (posted.TryDequeue(out var work))
                work.Callback(work.State);
        }
    }

    [Fact]
    public void The_view_asks_first_and_the_log_is_empty() => UiThread.Run(() =>
    {
        var view = View();

        Assert.True(view.IsAsking);
        Assert.False(view.HasLog);
        Assert.Null(view.StatusText);
        Assert.Equal("Close", view.CloseText);
    });

    [Fact]
    public void Each_line_of_the_work_shows_with_its_time_and_level() => UiThread.Run(() =>
    {
        var view = View();
        view.Start();

        view.Add(Line(@"Moving aside 'C:\Game\Win64\ue4ss'"));
        view.Add(Line(@"'C:\Game\Win64\ue4ss' could not be moved aside: Access is denied.", "WARN"));

        Assert.True(view.IsWorking);
        Assert.Equal("""
            14:05:09  INFO   Moving aside 'C:\Game\Win64\ue4ss'
            14:05:09  WARN   'C:\Game\Win64\ue4ss' could not be moved aside: Access is denied.

            """.ReplaceLineEndings("\n"), view.Document.Text);
    });

    [Fact]
    public void Lines_from_the_work_wait_for_the_window_and_one_turn_takes_them_all() => UiThread.Run(() =>
    {
        var window = new LaterContext();
        SynchronizationContext.SetSynchronizationContext(window);
        var view = View();
        view.Start();

        var work = new Thread(() =>
        {
            for (var i = 0; i < 500; i++)
                view.Add(Line($"Moving aside 'C:\\Game\\~mods\\{i}.pak'"));
        });
        work.Start();
        work.Join();

        Assert.Equal(0, view.Document.TextLength);
        Assert.Equal(1, window.Count);
        window.RunAll();
        Assert.Equal(500, view.Document.LineCount - 1);
    });

    [Fact]
    public void A_failure_keeps_the_view_open_with_the_reason() => UiThread.Run(() =>
    {
        var view = View();
        view.Start();
        view.Add(Line("Turning off 2 mod(s)."));

        view.Finish("Nothing was removed from the game.", failed: true);

        Assert.True(view.HasFailed);
        Assert.True(view.IsFinished);
        Assert.False(view.Closed.IsCompleted);
        Assert.Equal("Nothing was removed from the game.", view.StatusText);
        view.CloseCommand.Execute(null);
        Assert.True(view.Closed.IsCompleted);
    });

    [Fact]
    public void After_a_reset_of_the_settings_Close_starts_the_app_again() => UiThread.Run(() =>
    {
        var view = View();
        view.ResetSettings = true;
        Assert.Equal("Close", view.CloseText);
        view.Start();

        view.Finish("Modding reset.", failed: false);

        Assert.Equal("Start again", view.CloseText);
    });

    [Fact]
    public void Keep_my_version_closes_the_view_and_keeps_the_files() => UiThread.Run(() =>
    {
        var view = View();
        var kept = 0;
        view.Start();
        view.OfferKeepVersion(() => kept++);
        view.Finish("These files were changed outside the manager, so nothing was removed: a.ini.", failed: true);

        Assert.True(view.CanKeepVersion);
        view.KeepVersionCommand.Execute(null);

        Assert.True(view.Closed.IsCompleted);
        Assert.Equal(1, kept);
    });

    [Fact]
    public void The_log_is_copied_or_saved_only_on_a_click() => UiThread.Run(() =>
    {
        var file = Path.Combine(Path.GetTempPath(), $"uemm-reset-{Guid.NewGuid():N}.txt");
        var copied = new List<string>();
        var view = View();
        view.CopyText = copied.Add;
        view.PickSaveFile = name =>
        {
            Assert.StartsWith("reset-log-", name);
            return file;
        };
        view.Start();
        view.Add(Line("Reset modding of 'C:\\Game'."));
        view.Finish("Modding reset.", failed: false);
        Assert.Empty(copied);

        try
        {
            view.CopyLogCommand.Execute(null);
            view.SaveLogCommand.Execute(null);

            Assert.Equal(view.Document.Text, Assert.Single(copied));
            Assert.Equal(view.Document.Text, File.ReadAllText(file));
            Assert.Equal($"Saved: {file}", view.Feedback);
        }
        finally
        {
            File.Delete(file);
        }
    });

    [Theory]
    [InlineData(@"14:05:09  INFO   Moving aside 'C:\Game\Win64\dwmapi.dll'", @"14:05:09  INFO=Comment|'C:\Game\Win64\dwmapi.dll'=String")]
    [InlineData(@"14:05:09  WARN   In use or protected: 'C:\Game\Win64\ue4ss\UE4SS.log' (x)", @"14:05:09=Comment|WARN=Key|'C:\Game\Win64\ue4ss\UE4SS.log'=String")]
    [InlineData("14:05:09  ERROR  Reset modding stopped.", "14:05:09=Comment|ERROR=Key")]
    public void The_log_colours_its_parts(string line, string expected) => UiThread.Run(() =>
    {
        var highlighter = new DocumentHighlighter(new TextDocument(line), Syntax.Build("Log", _ => null));

        var parts = highlighter.HighlightLine(1).Sections
            .Select(section => $"{line.Substring(section.Offset, section.Length)}={section.Color.Name}");

        Assert.Equal(expected, string.Join("|", parts));
    });
}
