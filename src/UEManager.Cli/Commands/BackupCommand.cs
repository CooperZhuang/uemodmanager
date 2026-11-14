using System.Globalization;
using UEManager.Core.Games;
using UEManager.Core.Storage;

namespace UEManager.Cli.Commands;

/// <summary>
/// `--cli backup ...`: the app's Save backups page without a window. The zips live in the data folder like the page's,
/// so the page and this command see the same backups, with the same notes and the same personal ones.
/// </summary>
internal static class BackupCommand
{
    private static readonly string[] DataOptions = ["--data-dir", "--game", "--game-root"];

    public static int Run(CliArguments arguments)
    {
        if (arguments.Positionals.Count == 0)
            throw new CliError("invalid-arguments", "The backup command needs a subcommand.",
                details: ["Subcommands: list, create, restore, personal, note, delete.", "Run --help for the usage."]);
        var subcommand = arguments.Positionals[0].ToLowerInvariant();
        var rest = arguments.Positionals.Skip(1).ToList();
        return subcommand switch
        {
            "list" => List(arguments, rest),
            "create" => Create(arguments, rest),
            "restore" => Restore(arguments, rest),
            "personal" => Personal(arguments, rest),
            "note" => Note(arguments, rest),
            "delete" => Delete(arguments, rest),
            _ => throw new CliError("invalid-arguments", $"Unknown backup subcommand '{subcommand}'.",
                details: ["Known subcommands: list, create, restore, personal, note, delete."]),
        };
    }

    /// <summary>`backup list`: the normal and the personal backups, newest first, each with its note.</summary>
    private static int List(CliArguments arguments, List<string> rest)
    {
        arguments.Allow(["--json"], DataOptions);
        RequireNone("list", rest);
        var context = CliContext.Open(arguments, needInstall: false, write: false);
        var backupDir = context.Paths.SaveBackupDir(context.GameId);
        var backups = Items(context, out var personalCount);
        var payload = new BackupListPayload(1, "backup list", context.DataDir, context.GameId, backupDir, backups.Count,
            personalCount, backups);

        if (arguments.Json)
        {
            Output.Json(payload);
            return 0;
        }

        Output.Line($"Save backups in {backupDir}: {payload.Count}, of them {personalCount} personal");
        foreach (var backup in backups)
        {
            var kind = backup.Personal ? "[personal]" : "[normal]  ";
            var line = $"  {kind}  {backup.Name}  {Output.Time(backup.CreatedAt)}  {Output.Size(backup.SizeBytes),-9}";
            Output.Line(backup.Note is null ? line : $"{line}  Note: {backup.Note}");
        }
        return 0;
    }

    /// <summary>`backup create`: zips the save folder now, then keeps only the newest normal backups.</summary>
    private static int Create(CliArguments arguments, List<string> rest)
    {
        arguments.Allow(["--json"], [.. DataOptions, "--save-folder", "--note", "--keep"]);
        RequireNone("create", rest);
        var givenKeep = arguments.Keep is null ? (int?)null : ParseKeep(arguments.Keep);
        var context = CliContext.Open(arguments, needInstall: arguments.SaveFolder is null, write: true);
        var saveFolder = ResolveSaveFolder(context, arguments);
        if (!Directory.Exists(saveFolder))
        {
            throw new CliError("save-folder-not-found", $"The save folder '{saveFolder}' does not exist.",
                details: ["The game makes it at its first save; pass --save-folder <folder> to use another one.",
                    "Nothing was changed."]);
        }
        if (!SaveBackups.HasSaves(saveFolder))
        {
            throw new CliError("save-folder-empty", $"The save folder '{saveFolder}' holds no files.",
                details: ["There is nothing to back up yet.", "Nothing was changed."]);
        }

        var keep = givenKeep ?? context.Settings.SaveBackupKeep;
        var backupDir = context.Paths.SaveBackupDir(context.GameId);
        var before = SaveBackups.List(backupDir).ToHashSet(StringComparer.OrdinalIgnoreCase);
        string backup;
        try
        {
            backup = SaveBackups.Create(saveFolder, backupDir, DateTime.Now, keep, arguments.Note);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            throw new CliError("backup-failed", $"The save backup failed: {error.Message}");
        }
        var removed = before.Where(path => !File.Exists(path)).Select(Path.GetFileName).OfType<string>().ToList();
        var payload = new BackupCreatePayload(1, "backup create", context.DataDir, context.GameId, saveFolder, keep,
            Item(backupDir, backup, SaveBackups.Notes(backupDir), personal: false), removed);

        if (arguments.Json)
        {
            Output.Json(payload);
            return 0;
        }

        Output.Line($"Backed up the saves in '{saveFolder}' to '{payload.Backup.Name}' ({Output.Size(payload.Backup.SizeBytes)}).");
        if (payload.Backup.Note is not null)
            Output.Line($"  Note: {payload.Backup.Note}");
        Output.Line($"  Keeping {keep} normal backup(s).");
        if (removed.Count > 0)
        {
            Output.Line($"  Removed {removed.Count} older backup(s):");
            foreach (var name in removed)
                Output.Line($"    {name}");
        }
        return 0;
    }

    /// <summary>
    /// `backup restore`: puts a backup back, after backing up the saves of now; without --yes it only says what it would
    /// do. It takes no --save-folder: a restore makes the folder hold exactly the backup's files, so it only goes into
    /// the save folder the app uses, never one a typo names.
    /// </summary>
    private static int Restore(CliArguments arguments, List<string> rest)
    {
        arguments.Allow(["--json", "--yes"], DataOptions);
        var query = OneName("restore", rest);
        var context = CliContext.Open(arguments, needInstall: true, write: arguments.Yes);
        // Core's rule, which the app's Save backups page follows too; Restore asks it again before it changes anything.
        if (!SaveBackups.CanRestoreNow(context.Game))
        {
            throw new CliError("game-running", $"{context.Profile.DisplayName} is running.",
                details: ["Close the game first, then run the command again.", "Nothing was changed."]);
        }
        var backup = Find(context, query);
        var saveFolder = ResolveSaveFolder(context, arguments);
        var backupDir = context.Paths.SaveBackupDir(context.GameId);

        if (!arguments.Yes)
        {
            var preview = new BackupRestorePayload(1, "backup restore", context.DataDir, context.GameId, saveFolder, false,
                backup, null);
            if (arguments.Json)
                Output.Json(preview);
            else
                WriteRestorePreview(preview);
            Console.Error.WriteLine($"{Program.ToolName}: nothing was restored. Run the same command with --yes to restore it.");
            return Program.ConfirmationRequired;
        }

        var before = SaveBackups.Restore(backup.Path, saveFolder, backupDir, DateTime.Now, context.Settings.SaveBackupKeep,
            context.Game);
        var beforeBackup = before is not null && File.Exists(before)
            ? Item(backupDir, before, SaveBackups.Notes(backupDir), personal: false)
            : null;
        var payload = new BackupRestorePayload(1, "backup restore", context.DataDir, context.GameId, saveFolder, true, backup,
            beforeBackup);

        if (arguments.Json)
        {
            Output.Json(payload);
            return 0;
        }
        Output.Line($"Put back the saves of {Output.Time(backup.CreatedAt)} from '{backup.Name}' into '{saveFolder}'.");
        Output.Line(beforeBackup is null
            ? "There were no saves to back up first."
            : $"The saves of now were backed up to '{beforeBackup.Name}' first.");
        Output.Line(SteamCloudNote);
        return 0;
    }

    /// <summary>`backup personal`: keeps a backup for good (--on), or moves it back to the normal ones (--off).</summary>
    private static int Personal(CliArguments arguments, List<string> rest)
    {
        arguments.Allow(["--json", "--on", "--off"], DataOptions);
        if (arguments.On == arguments.Off)
        {
            throw new CliError("invalid-arguments", "The backup personal command needs exactly one of --on and --off.",
                details: ["--on keeps the backup for good; --off moves it back to the normal ones."]);
        }
        var query = OneName("personal", rest);
        var context = CliContext.Open(arguments, needInstall: false, write: true);
        var backup = Find(context, query);
        var backupDir = context.Paths.SaveBackupDir(context.GameId);
        var moved = SaveBackups.SetPersonal(backupDir, backup.Path, arguments.On);
        var changed = !moved.Equals(backup.Path, StringComparison.OrdinalIgnoreCase);
        var after = Item(backupDir, moved, SaveBackups.Notes(backupDir), personal: arguments.On);
        var payload = new BackupPersonalPayload(1, "backup personal", context.DataDir, context.GameId, after, changed);

        if (arguments.Json)
        {
            Output.Json(payload);
            return 0;
        }
        if (!changed)
            Output.Line($"'{after.Name}' is already a {(arguments.On ? "personal" : "normal")} backup.");
        else if (arguments.On)
            Output.Line($"Moved '{after.Name}' to the personal backups: they are kept until you delete them.");
        else
            Output.Line($"Moved '{after.Name}' back to the normal backups: the oldest go with the next backup.");
        return 0;
    }

    /// <summary>`backup note`: sets a backup's note, or removes it with --clear.</summary>
    private static int Note(CliArguments arguments, List<string> rest)
    {
        arguments.Allow(["--json", "--clear"], DataOptions);
        if (rest.Count == 0)
        {
            throw new CliError("invalid-arguments", "The backup note command needs a backup's name.",
                details: ["Run 'UEModManager.exe --cli backup list' for the names."]);
        }
        string? text;
        if (arguments.Clear)
        {
            if (rest.Count > 1)
            {
                throw new CliError("invalid-arguments", "Option '--clear' removes the note, so it takes no text.",
                    details: ["Give a name and --clear, or a name and the new note."]);
            }
            text = null;
        }
        else
        {
            if (rest.Count == 1)
            {
                throw new CliError("invalid-arguments", "The backup note command needs the new note, or --clear.",
                    details: ["Example: backup note \"saves 2026-10-27 21-00-00.zip\" \"Before the final boss\""]);
            }
            if (rest.Count > 2)
            {
                throw new CliError("invalid-arguments", $"The backup note command takes one name and one note, but "
                    + $"{rest.Count} arguments were given.", details: [.. rest.Select(p => $"  {p}")]);
            }
            text = rest[1];
        }

        var context = CliContext.Open(arguments, needInstall: false, write: true);
        var backup = Find(context, query: rest[0]);
        var backupDir = context.Paths.SaveBackupDir(context.GameId);
        var before = SaveBackups.Notes(backupDir).GetValueOrDefault(SaveBackups.Key(backupDir, backup.Path));
        SaveBackups.SetNote(backupDir, backup.Path, text);
        var changed = !string.Equals(before, text, StringComparison.Ordinal);
        var after = Item(backupDir, backup.Path, SaveBackups.Notes(backupDir), backup.Personal);
        var payload = new BackupNotePayload(1, "backup note", context.DataDir, context.GameId, after, changed);

        if (arguments.Json)
        {
            Output.Json(payload);
            return 0;
        }
        if (after.Note is null)
            Output.Line(changed ? $"Cleared the note on '{after.Name}'." : $"'{after.Name}' has no note.");
        else
            Output.Line(changed ? $"Note on '{after.Name}': {after.Note}" : $"'{after.Name}' already has that note.");
        return 0;
    }

    /// <summary>`backup delete`: removes a backup; without --yes it only says what it would delete.</summary>
    private static int Delete(CliArguments arguments, List<string> rest)
    {
        arguments.Allow(["--json", "--yes"], DataOptions);
        var query = OneName("delete", rest);
        var context = CliContext.Open(arguments, needInstall: false, write: arguments.Yes);
        var backup = Find(context, query);
        var backupDir = context.Paths.SaveBackupDir(context.GameId);

        if (!arguments.Yes)
        {
            var preview = new BackupDeletePayload(1, "backup delete", context.DataDir, context.GameId, false, backup);
            if (arguments.Json)
                Output.Json(preview);
            else
                WriteDeletePreview(preview);
            Console.Error.WriteLine($"{Program.ToolName}: nothing was deleted. Run the same command with --yes to delete it.");
            return Program.ConfirmationRequired;
        }

        SaveBackups.Delete(backupDir, backup.Path);
        var payload = new BackupDeletePayload(1, "backup delete", context.DataDir, context.GameId, true, backup);
        if (arguments.Json)
        {
            Output.Json(payload);
            return 0;
        }
        Output.Line($"Deleted the save backup '{backup.Name}'.");
        return 0;
    }

    /// <summary>
    /// The save folder: --save-folder (backup create only), else as the app resolves it (settings.json's choice for the
    /// game, then the profile's folder, which may be relative to the install folder).
    /// </summary>
    /// <exception cref="CliError">The game profile names no save folder and --save-folder was not given.</exception>
    private static string ResolveSaveFolder(CliContext context, CliArguments arguments)
    {
        if (arguments.SaveFolder is { } given)
            return Path.GetFullPath(given);
        var chosen = context.Settings.SaveFolders.GetValueOrDefault(context.GameId);
        return SaveBackups.Folder(context.Profile, context.Game, chosen)
            ?? throw new CliError("save-folder-not-found",
                $"Save backups are not available for {context.Profile.DisplayName}: its game profile names no save folder.",
                details: ["Pass --save-folder <folder> to use one anyway.", "Nothing was changed."]);
    }

    /// <summary>The normal and the personal backups, each group newest first.</summary>
    private static List<SaveBackupDto> Items(CliContext context, out int personalCount)
    {
        var backupDir = context.Paths.SaveBackupDir(context.GameId);
        var notes = SaveBackups.Notes(backupDir);
        var personal = SaveBackups.ListPersonal(backupDir);
        personalCount = personal.Count;
        return
        [
            .. SaveBackups.List(backupDir).Select(path => Item(backupDir, path, notes, personal: false)),
            .. personal.Select(path => Item(backupDir, path, notes, personal: true)),
        ];
    }

    private static SaveBackupDto Item(string backupDir, string path, IReadOnlyDictionary<string, string> notes, bool personal) =>
        new(Path.GetFileName(path), path, personal, new DateTimeOffset(File.GetLastWriteTime(path)), new FileInfo(path).Length,
            notes.GetValueOrDefault(SaveBackups.Key(backupDir, path)));

    /// <summary>The one backup a name means: its file name, without the extension, its path, or its place below the backup folder.</summary>
    /// <exception cref="CliError">None matches, or more than one does.</exception>
    private static SaveBackupDto Find(CliContext context, string query)
    {
        var backupDir = context.Paths.SaveBackupDir(context.GameId);
        var items = Items(context, out _);
        var matches = items.Where(backup => Matches(backupDir, backup, query)).ToList();
        if (matches.Count == 1)
            return matches[0];
        if (matches.Count == 0)
        {
            throw new CliError("backup-not-found", $"No save backup is named '{query}'.",
                details: items.Count == 0
                    ? ["There are no save backups yet; run 'backup create' first."]
                    : [.. items.Select(backup => $"{backup.Name}{(backup.Personal ? " (personal)" : "")}")]);
        }
        throw new CliError("ambiguous-backup", $"'{query}' names {matches.Count} save backups.",
            details: [.. matches.Select(backup => backup.Path)]);
    }

    private static bool Matches(string backupDir, SaveBackupDto backup, string query)
    {
        var key = SaveBackups.Key(backupDir, backup.Path).Replace('\\', '/');
        var given = query.Replace('\\', '/');
        return given.Equals(backup.Name, StringComparison.OrdinalIgnoreCase)
            || given.Equals(Path.GetFileNameWithoutExtension(backup.Name), StringComparison.OrdinalIgnoreCase)
            || given.Equals(key, StringComparison.OrdinalIgnoreCase)
            || given.Equals(Path.GetFileNameWithoutExtension(key), StringComparison.OrdinalIgnoreCase)
            // A full path, as list prints it, with either kind of slash: compared as given, not with the slashes turned.
            || (Path.IsPathFullyQualified(query)
                && Path.GetFullPath(query).Equals(Path.GetFullPath(backup.Path), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The number of normal backups to keep after a backup this command makes: one the app's Settings slider offers,
    /// 2 to 20 in steps of 2 (<see cref="SaveBackups.Normalize"/>), so a typo never removes all but one backup.
    /// </summary>
    /// <exception cref="CliError">The given text is not one of those numbers.</exception>
    private static int ParseKeep(string text) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var keep) && SaveBackups.Normalize(keep) == keep
            ? keep
            : throw new CliError("invalid-arguments",
                $"Option '--keep' needs an even number from {SaveBackups.MinKeep} to {SaveBackups.MaxKeep}, as the app's Settings offer, but was '{text}'.",
                details: [$"Example: --keep {SaveBackups.DefaultKeep}"]);

    /// <exception cref="CliError">The command takes no arguments.</exception>
    private static void RequireNone(string subcommand, List<string> rest)
    {
        if (rest.Count == 0)
            return;
        throw new CliError("invalid-arguments", $"The backup {subcommand} command takes no arguments, but {rest.Count} were given.",
            details: [.. rest.Select(argument => $"  {argument}")]);
    }

    /// <exception cref="CliError">The command does not have exactly one backup name.</exception>
    private static string OneName(string subcommand, List<string> rest) => rest.Count switch
    {
        1 => rest[0],
        0 => throw new CliError("invalid-arguments", $"The backup {subcommand} command needs a backup's name.",
            details: ["Run 'UEModManager.exe --cli backup list' for the names."]),
        _ => throw new CliError("invalid-arguments",
            $"The backup {subcommand} command takes one backup's name, but {rest.Count} were given.",
            details: [.. rest.Select(argument => $"  {argument}")]),
    };

    /// <summary>The app's note after its restore question: the cloud may offer the older saves back.</summary>
    private const string SteamCloudNote = "If Steam then asks about a conflict with its cloud saves, choose the files on this PC.";

    private static void WriteRestorePreview(BackupRestorePayload payload)
    {
        Output.Line($"Would put back the saves of {Output.Time(payload.Backup.CreatedAt)} from '{payload.Backup.Name}' "
            + $"into '{payload.SaveFolder}'.");
        Output.Line("The saves of now are backed up first, so the restore can be undone.");
        Output.Line(SteamCloudNote);
    }

    private static void WriteDeletePreview(BackupDeletePayload payload)
    {
        var backup = payload.Backup;
        Output.Line($"Would delete the save backup '{backup.Name}' ({Output.Time(backup.CreatedAt)}, "
            + $"{Output.Size(backup.SizeBytes)}{(backup.Personal ? ", personal" : "")}). This cannot be undone.");
        Output.Line("Your current saves stay as they are.");
    }
}
