using System.IO.Compression;
using System.Text.Json;
using UEManager.Core.Mods;
using UEManager.Core.Storage;

namespace UEManager.Core.Games;

/// <summary>
/// Zips of the game's save folder, made before the game starts, because a broken mod can break a save.
/// The player chooses how many of the newest backups to keep, and can put one back. A backup the player keeps moves
/// to the personal ones (<see cref="PersonalFolder"/>), which the count never removes and does not include; any backup
/// can carry a note of the player's.
/// </summary>
public static class SaveBackups
{
    /// <summary>
    /// How many backups a game keeps until the player chooses: 6, the step of the Settings slider above the 5 that
    /// versions up to 2.0.0 kept, so an update removes none of them.
    /// </summary>
    public const int DefaultKeep = 6;

    /// <summary>The Settings slider: 2 to 20 backups, in steps of 2.</summary>
    public const int MinKeep = 2, MaxKeep = 20, KeepStep = 2;

    /// <summary>
    /// A number of backups to keep as the slider has it: within 2 to 20, an odd one rounded up to the next step, so the
    /// player never loses a backup by it. Nothing set, 0 or less, is the default.
    /// </summary>
    public static int Normalize(int keep) =>
        keep < 1 ? DefaultKeep : Math.Clamp(keep + keep % KeepStep, MinKeep, MaxKeep);

    private const string Extension = ".zip";

    /// <summary>The folder below the backups that holds the personal ones: kept for good, and not part of the count.</summary>
    public const string PersonalFolder = "personal";

    /// <summary>The players' notes on the backups, by path below the backup folder, such as "personal/saves ….zip".</summary>
    private const string NotesFile = "notes.json";

    public static string PersonalDir(string backupDir) => Path.Combine(backupDir, PersonalFolder);

    /// <summary>
    /// Save backups are offered only when the game profile names the save folder. Games keep their saves in
    /// different places (AppData, Documents\My Games, a folder with a name of their own), so it is never guessed.
    /// </summary>
    public static bool IsAvailable(GameProfile profile) => !string.IsNullOrWhiteSpace(profile.SaveFolder);

    /// <summary>
    /// The save folder of a game: the player's choice, else the profile's, with %LOCALAPPDATA% and the like
    /// expanded. Null when save backups are not available for the game, or when the profile's folder is
    /// inside the install folder and the game was not found.
    /// </summary>
    public static string? Folder(GameProfile profile, GameInstall? game, string? chosen)
    {
        if (!IsAvailable(profile))
            return null;
        if (!string.IsNullOrWhiteSpace(chosen))
            return chosen;
        var folder = Environment.ExpandEnvironmentVariables(profile.SaveFolder!);
        if (Path.IsPathFullyQualified(folder))
            return Path.GetFullPath(folder);
        return game is null ? null : GameProfile.ResolvePath(game.RootPath, folder, game.ProjectFolder);
    }

    /// <summary>Whether the save folder holds any file. A new player has none: the game makes the folder at its first save.</summary>
    public static bool HasSaves(string saveFolder) =>
        Directory.Exists(saveFolder) && Directory.EnumerateFiles(saveFolder, "*", SearchOption.AllDirectories).Any();

    /// <summary>
    /// Zips the save folder into a new backup, with the player's note when there is one, then deletes all but the
    /// newest <paramref name="keep"/> of the normal backups. The personal ones are never deleted here.
    /// </summary>
    /// <returns>The new backup.</returns>
    public static string Create(string saveFolder, string backupDir, DateTime now, int keep = DefaultKeep, string? note = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(keep, 1);
        if (!Directory.Exists(saveFolder))
            throw new DirectoryNotFoundException($"The save folder was not found: {saveFolder}");
        if (!HasSaves(saveFolder))
            throw new IOException($"The save folder is empty: {saveFolder}");

        Directory.CreateDirectory(backupDir);
        var backup = Path.Combine(backupDir, $"saves {now:yyyy-MM-dd HH-mm-ss}{Extension}");
        for (var i = 2; File.Exists(backup); i++)
            backup = Path.Combine(backupDir, $"saves {now:yyyy-MM-dd HH-mm-ss} ({i}){Extension}");
        // Written under another name first, so a failed zip never counts as a backup.
        var temp = backup + ".tmp";
        try
        {
            ZipFile.CreateFromDirectory(saveFolder, temp, CompressionLevel.Optimal, includeBaseDirectory: false);
            File.Move(temp, backup);
        }
        catch
        {
            FileSystem.TryDeleteFile(temp);
            throw;
        }
        var old = List(backupDir).Skip(keep).ToList();
        foreach (var file in old)
            FileSystem.TryDeleteFile(file);
        TryUpdateNotes(backupDir, notes =>
        {
            foreach (var file in old.Where(file => !File.Exists(file)))
                notes.Remove(Key(backupDir, file));
            if (!string.IsNullOrWhiteSpace(note))
                notes[Key(backupDir, backup)] = note.Trim();
        });
        Log.Info($"Backed up the saves in '{saveFolder}' to '{backup}'.");
        return backup;
    }

    /// <summary>
    /// Moves a backup to the personal ones (<paramref name="personal"/>), where the count never removes it, or back to
    /// the normal ones, where the oldest go with the next backup. Its time and its note go with it.
    /// </summary>
    /// <returns>The backup's new path.</returns>
    public static string SetPersonal(string backupDir, string backup, bool personal)
    {
        var target = personal ? PersonalDir(backupDir) : backupDir;
        if (FileSystem.IsSameFolder(Path.GetDirectoryName(backup)!, target))
            return backup;
        Directory.CreateDirectory(target);
        var name = Path.GetFileNameWithoutExtension(backup);
        var moved = Path.Combine(target, name + Extension);
        for (var i = 2; File.Exists(moved); i++)
            moved = Path.Combine(target, $"{name} ({i}){Extension}");
        File.Move(backup, moved);
        TryUpdateNotes(backupDir, notes =>
        {
            if (notes.Remove(Key(backupDir, backup), out var note))
                notes[Key(backupDir, moved)] = note;
        });
        Log.Info($"Moved the save backup '{backup}' to the {(personal ? "personal" : "normal")} ones: '{moved}'.");
        return moved;
    }

    /// <summary>Deletes a backup the player chose, with its note.</summary>
    public static void Delete(string backupDir, string backup)
    {
        File.Delete(backup);
        TryUpdateNotes(backupDir, notes => notes.Remove(Key(backupDir, backup)));
        Log.Info($"Deleted the save backup '{backup}'.");
    }

    /// <summary>The players' notes, by <see cref="Key"/>; empty when there are none or the file cannot be read.</summary>
    public static IReadOnlyDictionary<string, string> Notes(string backupDir) => ReadNotes(backupDir);

    /// <summary>Sets the player's note on a backup; an empty note removes it.</summary>
    public static void SetNote(string backupDir, string backup, string? note)
    {
        var notes = ReadNotes(backupDir);
        var key = Key(backupDir, backup);
        var text = note?.Trim();
        if (string.IsNullOrEmpty(text) ? !notes.Remove(key) : notes.TryGetValue(key, out var old) && old == text)
            return;
        if (!string.IsNullOrEmpty(text))
            notes[key] = text;
        JsonFile.Write(Path.Combine(backupDir, NotesFile), notes);
    }

    /// <summary>A backup's place in the notes: its path below the backup folder, with forward slashes.</summary>
    public static string Key(string backupDir, string backup) =>
        Path.GetRelativePath(Path.GetFullPath(backupDir), Path.GetFullPath(backup)).Replace('\\', '/');

    private static Dictionary<string, string> ReadNotes(string backupDir)
    {
        var file = Path.Combine(backupDir, NotesFile);
        try
        {
            return JsonFile.Read<Dictionary<string, string>>(file) is { } notes
                ? new Dictionary<string, string>(notes, StringComparer.OrdinalIgnoreCase)
                : new(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Log.Warn($"The notes of the save backups could not be read: {ex.Message}");
            return new(StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>Changes the notes after a backup moved, went or came; a note that cannot be written is not worth failing the backup for.</summary>
    private static void TryUpdateNotes(string backupDir, Action<Dictionary<string, string>> change)
    {
        var notes = ReadNotes(backupDir);
        var before = notes.Count == 0 ? "" : JsonSerializer.Serialize(notes);
        change(notes);
        if ((notes.Count == 0 ? "" : JsonSerializer.Serialize(notes)) == before)
            return;
        try
        {
            JsonFile.Write(Path.Combine(backupDir, NotesFile), notes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"The notes of the save backups could not be written: {ex.Message}");
        }
    }

    /// <summary>
    /// Whether the saves can be put back now: not while the game runs. The game keeps its saves and writes them again,
    /// so a restore would be undone or leave broken saves. The app asks before its question, the command line tool
    /// before its plan, and <see cref="Restore"/> once more right before it changes anything.
    /// </summary>
    public static bool CanRestoreNow(GameInstall? game) => game is null || !GameProcess.IsRunning(game);

    /// <summary>
    /// Puts a backup back: the save folder then holds exactly the backup's files. The saves of now are backed up
    /// first, so a restore can be undone. The backup is unpacked next to the save folder before anything changes,
    /// and the old save folder moves aside until the new one is in place; when a step fails, it moves back.
    /// </summary>
    /// <param name="game">The game the saves belong to: while it runs, nothing changes (see <see cref="CanRestoreNow"/>).</param>
    /// <returns>The backup of the saves before the restore, or null when there were none.</returns>
    public static string? Restore(string backup, string saveFolder, string backupDir, DateTime now, int keep = DefaultKeep,
        GameInstall? game = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(keep, 1);
        // Asked once more here: the game may have started while the player read the question.
        if (!CanRestoreNow(game))
            throw new ModException($"{game!.Profile.DisplayName} is running. Close the game first, then try again.");
        if (!File.Exists(backup))
            throw new ModException($"The save backup is gone: {backup}");
        var folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(saveFolder));
        // Next to the save folder, so both moves stay on one drive; names of their own, so an interrupted
        // restore never leaves anything another one would delete.
        var staging = $"{folder}.uemm-restore-{now:yyyyMMddHHmmss}";
        var aside = $"{folder}.uemm-old-{now:yyyyMMddHHmmss}";
        try
        {
            ZipFile.ExtractToDirectory(backup, staging);
            var before = HasSaves(folder) ? Create(folder, backupDir, now, keep) : null;
            if (Directory.Exists(folder))
                Directory.Move(folder, aside);
            try
            {
                Directory.Move(staging, folder);
            }
            catch
            {
                if (Directory.Exists(aside))
                    Directory.Move(aside, folder);
                throw;
            }
            FileSystem.TryDeleteDirectory(aside);
            Log.Info($"Restored the saves in '{folder}' from '{backup}'.");
            return before;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Log.Warn($"The saves could not be restored from '{backup}': {ex.Message}");
            throw new ModException($"The saves could not be put back, so they stay as they are. Close the game, then try again. ({ex.Message})");
        }
        finally
        {
            FileSystem.TryDeleteDirectory(staging);
        }
    }

    /// <summary>The personal backups, newest first.</summary>
    public static IReadOnlyList<string> ListPersonal(string backupDir) => List(PersonalDir(backupDir));

    /// <summary>The normal backups, newest first: the ones the count keeps. The personal ones are in a folder below.</summary>
    public static IReadOnlyList<string> List(string backupDir) =>
        Directory.Exists(backupDir)
            ? Directory.EnumerateFiles(backupDir, "*" + Extension)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .ThenByDescending(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList()
            : [];
}
