using System.IO;
using System.Windows.Threading;
using UEManager.Core.Games;
using UEManager.Core.Storage;
using UEManager.UI.Services;
using UEManager.UI.ViewModels;
using Xunit;

namespace UEManager.UI.Tests;

/// <summary>The Save backups page: how many normal backups a game keeps (a slider), Personal ones the count never removes, and notes.</summary>
public class SaveBackupsTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "uemm-save-backups-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(folder))
            Directory.Delete(folder, recursive: true);
    }

    private void WithApp(Action<MainViewModel, AppPaths> check) => UiThread.Run(() =>
    {
        var paths = new AppPaths(Path.Combine(folder, "Data"));
        paths.EnsureWritable();
        var saves = Path.Combine(folder, "Saves");
        Directory.CreateDirectory(saves);
        File.WriteAllText(Path.Combine(saves, "Slot1.sav"), "current saves");
        var profile = new GameProfile { Id = "test", DisplayName = "Phantom Blade Zero", SaveFolder = saves };
        check(new MainViewModel(profile, paths, new ArtSet(null, null)), paths);
    });

    [Fact]
    public void The_slider_saves_at_once_and_lowering_it_removes_nothing_yet() => WithApp((model, paths) =>
    {
        MakeBackups(paths, 8);
        Assert.Equal(6, model.SaveBackupKeep);

        model.SaveBackupKeep = 12;
        Assert.Equal(12, AppSettings.Load(paths).SaveBackupKeep);
        Assert.Equal(12, new MainViewModel(model.Profile, paths, new ArtSet(null, null)).SaveBackupKeep);

        model.SaveBackupKeep = 2;
        Assert.Equal(8, model.SaveBackupList.Count);
        Assert.StartsWith("8 backups, 2 after the next backup", model.SaveBackupsText);
    });

    [Theory]
    [InlineData(5, 6)]
    [InlineData(1, 2)]
    [InlineData(25, 20)]
    [InlineData(0, 6)]
    public void The_slider_takes_only_its_steps(int set, int kept) => WithApp((model, paths) =>
    {
        model.SaveBackupKeep = set;

        Assert.Equal(kept, model.SaveBackupKeep);
        Assert.Equal(kept, AppSettings.Load(paths).SaveBackupKeep);
    });

    [Fact]
    public void Keep_moves_a_backup_to_Personal_where_the_count_never_removes_it() => WithApp((model, paths) =>
    {
        MakeBackups(paths, 8);
        var oldest = model.SaveBackupList[^1].Path;

        model.KeepSaveBackupCommand.Execute(oldest);
        Assert.Equal(7, model.SaveBackupList.Count);
        var kept = Assert.Single(model.PersonalSaveBackupList);
        Assert.True(kept.IsPersonal);
        Assert.True(model.HasPersonalSaveBackups);

        // Two normal ones stay; the personal one is not one of them and stays too.
        model.SaveBackupKeep = 2;
        Complete(model.BackUpSavesNowCommand.ExecuteAsync(null));
        Assert.Equal(2, model.SaveBackupList.Count);
        Assert.True(File.Exists(kept.Path));

        model.ReleaseSaveBackupCommand.Execute(kept.Path);
        Assert.Empty(model.PersonalSaveBackupList);
        Assert.Equal(3, model.SaveBackupList.Count);
    });

    [Fact]
    public void A_note_saves_at_once_follows_its_backup_and_survives_a_restart() => WithApp((model, paths) =>
    {
        MakeBackups(paths, 3);
        model.SaveBackupList[0].Note = "  Before the final boss  ";
        Assert.Equal("Before the final boss", Reopen(model, paths).SaveBackupList[0].Note);

        model.KeepSaveBackupCommand.Execute(model.SaveBackupList[0].Path);
        Assert.Equal("Before the final boss", Reopen(model, paths).PersonalSaveBackupList[0].Note);

        // A new backup takes the note typed for it, and the field is empty again.
        model.NewSaveBackupNote = "A clean start";
        Complete(model.BackUpSavesNowCommand.ExecuteAsync(null));
        Assert.Equal("A clean start", model.SaveBackupList[0].Note);
        Assert.Equal("", model.NewSaveBackupNote);

        model.PersonalSaveBackupList[0].Note = "";
        Assert.Equal("", Reopen(model, paths).PersonalSaveBackupList[0].Note);
    });

    [Fact]
    public void A_backup_keeps_the_chosen_number_and_refreshes_the_list() => WithApp((model, paths) =>
    {
        MakeBackups(paths, 8);
        model.SaveBackupKeep = 4;
        var changed = new List<string?>();
        model.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        Complete(model.BackUpSavesNowCommand.ExecuteAsync(null));

        Assert.Equal(4, model.SaveBackupList.Count);
        Assert.Contains(nameof(model.SaveBackupList), changed);
        Assert.Contains(nameof(model.PersonalSaveBackupList), changed);
    });

    [Fact]
    public void Deleting_a_backup_asks_first_and_leaves_current_saves_and_other_backups_alone() => WithApp((model, paths) =>
    {
        MakeBackups(paths, 8);
        var backup = model.SaveBackupList[^1].Path;
        var cancelled = model.DeleteSaveBackupCommand.ExecuteAsync(backup);
        Assert.NotNull(model.Confirm);
        Assert.True(File.Exists(backup));
        model.Confirm.CancelCommand.Execute(null);
        Complete(cancelled);
        Assert.True(File.Exists(backup));

        var accepted = model.DeleteSaveBackupCommand.ExecuteAsync(backup);
        model.Confirm!.AcceptCommand.Execute(null);
        Complete(accepted);

        Assert.False(File.Exists(backup));
        Assert.Equal(7, model.SaveBackupList.Count);
        Assert.Equal("current saves", File.ReadAllText(Path.Combine(folder, "Saves", "Slot1.sav")));

        // A personal backup asks too.
        model.KeepSaveBackupCommand.Execute(model.SaveBackupList[^1].Path);
        var personal = model.PersonalSaveBackupList[0].Path;
        var asked = model.DeleteSaveBackupCommand.ExecuteAsync(personal);
        model.Confirm!.AcceptCommand.Execute(null);
        Complete(asked);
        Assert.False(File.Exists(personal));
        Assert.Empty(model.PersonalSaveBackupList);

        // A file outside this game's backup list cannot be deleted with the command.
        var save = Path.Combine(folder, "Saves", "Slot1.sav");
        Complete(model.DeleteSaveBackupCommand.ExecuteAsync(save));
        Assert.True(File.Exists(save));
        Assert.Null(model.Confirm);
    });

    private static MainViewModel Reopen(MainViewModel model, AppPaths paths) => new(model.Profile, paths, new ArtSet(null, null));

    private void MakeBackups(AppPaths paths, int count)
    {
        var start = new DateTime(2026, 10, 27, 21, 0, 0);
        for (var i = 0; i < count; i++)
        {
            var backup = SaveBackups.Create(Path.Combine(folder, "Saves"), paths.SaveBackupDir("test"), start.AddMinutes(i), count);
            File.SetLastWriteTimeUtc(backup, start.AddMinutes(i).ToUniversalTime());
        }
    }

    private static void Complete(Task task)
    {
        var frame = new DispatcherFrame();
        var dispatcher = Dispatcher.CurrentDispatcher;
        _ = task.ContinueWith(_ => dispatcher.BeginInvoke(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
        task.GetAwaiter().GetResult();
    }
}
