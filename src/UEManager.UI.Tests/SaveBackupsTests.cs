using System.IO;
using System.Windows.Threading;
using UEManager.Core.Games;
using UEManager.Core.Storage;
using UEManager.UI.Services;
using UEManager.UI.ViewModels;
using Xunit;

namespace UEManager.UI.Tests;

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
    public void Saving_retention_persists_it_without_deleting_existing_backups() => WithApp((model, paths) =>
    {
        MakeBackups(paths, 8);
        Assert.Equal("5", model.SaveBackupKeepText);
        model.SaveBackupKeepText = "12";
        Assert.True(model.SaveBackupRetentionCommand.CanExecute(null));
        model.SaveBackupRetentionCommand.Execute(null);
        Assert.Equal(12, AppSettings.Load(paths).SaveBackupKeep);
        Assert.Equal(12, new MainViewModel(model.Profile, paths, new ArtSet(null, null)).SaveBackupKeep);
        Assert.False(model.SaveBackupRetentionCommand.CanExecute(null));

        model.SaveBackupKeepText = "2";
        model.SaveBackupRetentionCommand.Execute(null);
        Assert.Equal(8, model.SaveBackupList.Count);
        Assert.Contains("8 of 2 kept", model.SaveBackupsText);
    });

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("1.5")]
    [InlineData("many")]
    [InlineData("2147483648")]
    public void Invalid_retention_cannot_be_saved(string value) => WithApp((model, _) =>
    {
        model.SaveBackupKeepText = value;
        Assert.False(model.SaveBackupRetentionCommand.CanExecute(null));
        model.SaveBackupRetentionCommand.Execute(null);
        Assert.Equal(5, model.SaveBackupKeep);
        Assert.Equal("Enter a whole number of 1 or more.", model.SaveBackupKeepHint);
    });

    [Fact]
    public void Settings_shows_five_and_the_management_list_has_every_backup() => WithApp((model, paths) =>
    {
        MakeBackups(paths, 8);
        Assert.True(model.HasMoreSaveBackups);
        Assert.Equal(8, model.SaveBackupList.Count);
        Assert.Equal(model.SaveBackupList.Take(5), model.RecentSaveBackupList);

        foreach (var backup in model.SaveBackupList.Skip(5))
            File.Delete(backup.Path);
        model.ReloadSaveBackupsCommand.Execute(null);
        Assert.False(model.HasMoreSaveBackups);
        Assert.Equal(5, model.SaveBackupList.Count);
    });

    [Fact]
    public void Manual_backup_uses_the_saved_retention_and_refreshes_the_preview() => WithApp((model, paths) =>
    {
        MakeBackups(paths, 8);
        model.SaveBackupKeepText = "3";
        model.SaveBackupRetentionCommand.Execute(null);
        var changed = new List<string?>();
        model.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        Complete(model.BackUpSavesNowCommand.ExecuteAsync(null));

        Assert.Equal(3, model.SaveBackupList.Count);
        Assert.False(model.HasMoreSaveBackups);
        Assert.Contains(nameof(model.RecentSaveBackupList), changed);
        Assert.Contains(nameof(model.HasMoreSaveBackups), changed);
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

        // A file outside this game's backup list cannot be deleted with the command.
        var save = Path.Combine(folder, "Saves", "Slot1.sav");
        Complete(model.DeleteSaveBackupCommand.ExecuteAsync(save));
        Assert.True(File.Exists(save));
        Assert.Null(model.Confirm);
    });

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
