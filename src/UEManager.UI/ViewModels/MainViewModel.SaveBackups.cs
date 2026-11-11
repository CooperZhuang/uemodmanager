using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace UEManager.UI.ViewModels;

public sealed partial class MainViewModel
{
    public const int SaveBackupPreviewCount = 5;

    public int SaveBackupKeep => settings.SaveBackupKeep;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SaveBackupKeepHint))]
    [NotifyCanExecuteChangedFor(nameof(SaveBackupRetentionCommand))]
    private string saveBackupKeepText = "";

    public string SaveBackupKeepHint => int.TryParse(SaveBackupKeepText, out var keep) && keep >= 1
        ? L.T("Older backups are removed after the next successful backup.")
        : L.T("Enter a whole number of 1 or more.");

    /// <summary>Settings stays short even when the player keeps many backups.</summary>
    public IReadOnlyList<SaveBackupItem> RecentSaveBackupList => SaveBackupList.Take(SaveBackupPreviewCount).ToList();

    public bool HasMoreSaveBackups => SaveBackupList.Count > SaveBackupPreviewCount;

    private bool CanSaveBackupRetention() =>
        int.TryParse(SaveBackupKeepText, out var keep) && keep >= 1 && keep != SaveBackupKeep;

    [RelayCommand(CanExecute = nameof(CanSaveBackupRetention))]
    private void SaveBackupRetention()
    {
        if (!CanSaveBackupRetention())
            return;
        settings.SaveBackupKeep = int.Parse(SaveBackupKeepText);
        SaveSettings();
        SaveBackupKeepText = SaveBackupKeep.ToString();
        OnPropertyChanged(nameof(SaveBackupKeep));
        SaveBackupRetentionCommand.NotifyCanExecuteChanged();
        RefreshSaveBackups();
    }

    [RelayCommand]
    private void ReloadSaveBackups() => RefreshSaveBackups();

    internal void RefreshSaveBackups()
    {
        OnPropertyChanged(nameof(SaveBackupsText));
        OnPropertyChanged(nameof(SaveBackupList));
        OnPropertyChanged(nameof(RecentSaveBackupList));
        OnPropertyChanged(nameof(HasMoreSaveBackups));
    }

    [RelayCommand]
    private async Task DeleteSaveBackup(string backup)
    {
        // Only a backup in this game's list can be removed through this command.
        if (!SaveBackupList.Any(item => string.Equals(item.Path, backup, StringComparison.OrdinalIgnoreCase)))
            return;
        var made = File.GetLastWriteTime(backup);
        if (!await AskAsync(L.T("Delete save backup"),
                L.F($"Delete the save backup of {made:G}? This cannot be undone. Your current saves stay as they are."),
                L.T("Delete")))
            return;
        if (await RunAsync(L.T("Deleting save backup…"), () => Task.Run(() => File.Delete(backup))))
            ShowToast(L.T("Save backup deleted."));
        RefreshSaveBackups();
    }
}
