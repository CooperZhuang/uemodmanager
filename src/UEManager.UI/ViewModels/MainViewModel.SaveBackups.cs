using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UEManager.Core;
using UEManager.Core.Games;
using UEManager.UI.Services;

namespace UEManager.UI.ViewModels;

/// <summary>
/// A save backup on the Save backups page: its file, when it was made and how big it is, the player's note, and
/// whether it is a personal one, which the count never removes.
/// </summary>
public sealed class SaveBackupItem : ObservableObject
{
    private readonly Action<SaveBackupItem, string>? noteChanged;
    private string note;

    public SaveBackupItem(string path, string text, string? note = null, bool isPersonal = false,
        Action<SaveBackupItem, string>? noteChanged = null)
    {
        Path = path;
        Text = text;
        this.note = note ?? "";
        IsPersonal = isPersonal;
        this.noteChanged = noteChanged;
    }

    public string Path { get; }

    public string Text { get; }

    public bool IsPersonal { get; }

    /// <summary>The player's note, such as "Before the final boss"; saved when the field loses the focus or on Enter.</summary>
    public string Note
    {
        get => note;
        set
        {
            if (SetProperty(ref note, value?.Trim() ?? ""))
                noteChanged?.Invoke(this, note);
        }
    }
}

/// <summary>
/// The Save backups page: the save folder, the backup at launch, how many normal backups a game keeps (a slider, 2 to 20
/// in steps of 2), and the backups in two columns: the normal ones, of which the oldest go, and the personal ones the
/// player keeps for good. Each can carry a note.
/// </summary>
public sealed partial class MainViewModel
{
    private string SaveBackupDir => paths.SaveBackupDir(Profile.Id);

    /// <summary>
    /// The number of normal backups to keep, saved at once. Lowering it removes nothing yet: the next backup removes the
    /// oldest, and only when it worked.
    /// </summary>
    public int SaveBackupKeep
    {
        get => settings.SaveBackupKeep;
        set
        {
            if (SaveBackups.Normalize(value) == settings.SaveBackupKeep)
                return;
            settings.SaveBackupKeep = value;
            SaveSettings();
            OnPropertyChanged();
            OnPropertyChanged(nameof(SaveBackupsText));
        }
    }

    public int SaveBackupKeepMin => SaveBackups.MinKeep;

    public int SaveBackupKeepMax => SaveBackups.MaxKeep;

    public int SaveBackupKeepStep => SaveBackups.KeepStep;

    /// <summary>The note for the next "Back up now", such as "Before the final boss"; empty for none.</summary>
    [ObservableProperty]
    private string newSaveBackupNote = "";

    /// <summary>The normal backups, newest first: the oldest go when there are more than <see cref="SaveBackupKeep"/>.</summary>
    public IReadOnlyList<SaveBackupItem> SaveBackupList => Items(SaveBackups.List(SaveBackupDir), personal: false);

    /// <summary>The personal backups, newest first: never removed by the count, and not part of it.</summary>
    public IReadOnlyList<SaveBackupItem> PersonalSaveBackupList => Items(SaveBackups.ListPersonal(SaveBackupDir), personal: true);

    public bool HasSaveBackups => SaveBackups.List(SaveBackupDir).Count > 0;

    public bool HasPersonalSaveBackups => SaveBackups.ListPersonal(SaveBackupDir).Count > 0;

    private List<SaveBackupItem> Items(IReadOnlyList<string> files, bool personal)
    {
        var notes = SaveBackups.Notes(SaveBackupDir);
        return files.Select(path => new SaveBackupItem(path,
                $"{File.GetLastWriteTime(path):G}  ·  {Format.Size(new FileInfo(path).Length)}",
                notes.GetValueOrDefault(SaveBackups.Key(SaveBackupDir, path)), personal, SaveNote))
            .ToList();
    }

    private void SaveNote(SaveBackupItem item, string note)
    {
        try
        {
            SaveBackups.SetNote(SaveBackupDir, item.Path, note);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"The note on '{item.Path}' could not be saved: {ex.Message}");
            ShowToast(L.F($"The note could not be saved: {ex.Message}"), isError: true);
        }
    }

    internal void RefreshSaveBackups()
    {
        OnPropertyChanged(nameof(SaveBackupsText));
        OnPropertyChanged(nameof(SaveBackupList));
        OnPropertyChanged(nameof(PersonalSaveBackupList));
        OnPropertyChanged(nameof(HasSaveBackups));
        OnPropertyChanged(nameof(HasPersonalSaveBackups));
    }

    /// <summary>Whether a path is one of this game's backups, normal or personal: only those can be changed here.</summary>
    private bool IsSaveBackup(string backup) =>
        SaveBackups.List(SaveBackupDir).Concat(SaveBackups.ListPersonal(SaveBackupDir))
            .Any(path => string.Equals(path, backup, StringComparison.OrdinalIgnoreCase));

    /// <summary>Keep: moves a backup to the personal ones, where the count never removes it.</summary>
    [RelayCommand]
    private void KeepSaveBackup(string backup) => MoveSaveBackup(backup, personal: true);

    /// <summary>Moves a personal backup back to the normal ones, where the oldest go with the next backup.</summary>
    [RelayCommand]
    private void ReleaseSaveBackup(string backup) => MoveSaveBackup(backup, personal: false);

    private void MoveSaveBackup(string backup, bool personal)
    {
        if (!IsSaveBackup(backup))
            return;
        try
        {
            SaveBackups.SetPersonal(SaveBackupDir, backup, personal);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"The save backup '{backup}' could not be moved: {ex.Message}");
            ShowToast(L.F($"The backup could not be moved: {ex.Message}"), isError: true);
        }
        RefreshSaveBackups();
    }

    [RelayCommand]
    private async Task DeleteSaveBackup(string backup)
    {
        // Only a backup in this game's lists can be removed through this command.
        if (!IsSaveBackup(backup))
            return;
        var made = File.GetLastWriteTime(backup);
        if (!await AskAsync(L.T("Delete save backup"),
                L.F($"Delete the save backup of {made:G}? This cannot be undone. Your current saves stay as they are."),
                L.T("Delete")))
            return;
        if (await RunAsync(L.T("Deleting save backup…"), () => Task.Run(() => SaveBackups.Delete(SaveBackupDir, backup))))
            ShowToast(L.T("Save backup deleted."));
        RefreshSaveBackups();
    }
}
