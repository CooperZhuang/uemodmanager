using System.IO.Compression;
using System.Text.Json;
using UEManager.Cli;
using UEManager.Core.Storage;

namespace UEManager.Cli.Tests;

/// <summary>
/// `--cli backup`, run in this process against a fake Phantom Blade Zero install and a data folder of its own: every
/// save and backup lives below a temp folder, so no real save is read. The console is shared with
/// <see cref="CliTests"/>, so both classes are in the "Console" collection and never run at the same time.
/// </summary>
[Collection("Console")]
public sealed class BackupCommandTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "uemm-cli-backup-tests", Guid.NewGuid().ToString("N"));

    private string Data => Path.Combine(root, "Data");

    private string GameRoot => Path.Combine(root, "Game");

    private string Saves => Path.Combine(GameRoot, "ProjectPBZ", "Saved", "SaveGames");

    private string BackupDir => Path.Combine(Data, "games", "phantom-blade-zero", "save-backups");

    public BackupCommandTests()
    {
        // What the game's profile needs to know an install: the Engine folder, the project folder and the exe. The
        // profile's save folder is {Project}/Saved/SaveGames, so this fake install has a save folder of its own.
        Directory.CreateDirectory(Path.Combine(GameRoot, "Engine"));
        Directory.CreateDirectory(Path.Combine(GameRoot, "ProjectPBZ", "Content", "Paks"));
        Write(Path.Combine(GameRoot, "platform", "steam", "PBZ.exe"), "the game");
        Write(Path.Combine(Saves, "Slot1.sav"), "slot one");
        Directory.CreateDirectory(Data);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch (IOException)
        {
            // The temp folder is cleaned by Windows.
        }
    }

    private static void Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    /// <summary>Runs one command with its output caught, as a script would see it.</summary>
    private static (int Code, string Out, string Error) Raw(params string[] args)
    {
        var (output, error) = (new StringWriter(), new StringWriter());
        var (oldOutput, oldError) = (Console.Out, Console.Error);
        Console.SetOut(output);
        Console.SetError(error);
        try
        {
            return (Program.Main(args), output.ToString(), error.ToString());
        }
        finally
        {
            Console.SetOut(oldOutput);
            Console.SetError(oldError);
        }
    }

    private (int Code, string Out, string Error) Run(params string[] args) =>
        Raw([.. args, "--data-dir", Data, "--game", "phantom-blade-zero", "--game-root", GameRoot]);

    private static JsonElement Json(string text)
    {
        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }

    /// <summary>`backup create --json`, required to work; the payload's root.</summary>
    private JsonElement Created(string? note = null, string? keep = null)
    {
        var args = new List<string> { "backup", "create", "--json" };
        if (note is not null)
            args.AddRange(["--note", note]);
        if (keep is not null)
            args.AddRange(["--keep", keep]);
        var (code, output, error) = Run([.. args]);
        Assert.True(code == 0, error);
        return Json(output);
    }

    private static string NameOf(JsonElement payload) => payload.GetProperty("backup").GetProperty("name").GetString()!;

    private static string PathOf(JsonElement payload) => payload.GetProperty("backup").GetProperty("path").GetString()!;

    [Fact]
    public void A_backup_is_created_listed_with_its_note_and_holds_the_saves()
    {
        var created = Created(note: "Before the final boss");
        var name = NameOf(created);
        var path = PathOf(created);

        Assert.Equal(1, created.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("backup create", created.GetProperty("command").GetString());
        Assert.Equal(Path.GetFullPath(Saves), created.GetProperty("saveFolder").GetString());
        Assert.Equal(6, created.GetProperty("keep").GetInt32());
        Assert.False(created.GetProperty("backup").GetProperty("personal").GetBoolean());
        Assert.Equal("Before the final boss", created.GetProperty("backup").GetProperty("note").GetString());
        Assert.True(created.GetProperty("backup").GetProperty("sizeBytes").GetInt64() > 0);
        Assert.EndsWith(".zip", name);
        Assert.True(File.Exists(path));
        using (var zip = ZipFile.OpenRead(path))
            Assert.Equal(["Slot1.sav"], zip.Entries.Select(entry => entry.FullName.Replace('\\', '/')));

        var (code, output, error) = Run("backup", "list", "--json");
        Assert.True(code == 0, error);
        var list = Json(output);
        Assert.Equal("backup list", list.GetProperty("command").GetString());
        Assert.Equal(1, list.GetProperty("count").GetInt32());
        Assert.Equal(0, list.GetProperty("personalCount").GetInt32());
        var item = list.GetProperty("backups")[0];
        Assert.Equal(name, item.GetProperty("name").GetString());
        Assert.Equal(path, item.GetProperty("path").GetString());
        Assert.Equal("Before the final boss", item.GetProperty("note").GetString());
        Assert.Equal(created.GetProperty("backup").GetProperty("createdAt").GetString(),
            item.GetProperty("createdAt").GetString());

        var (textCode, text, _) = Run("backup", "list");
        Assert.Equal(0, textCode);
        Assert.Contains(name, text);
        Assert.Contains("Before the final boss", text);
    }

    [Fact]
    public void A_backup_can_be_made_personal_and_normal_again()
    {
        var name = NameOf(Created());

        var (onCode, onOutput, onError) = Run("backup", "personal", name, "--on", "--json");
        Assert.True(onCode == 0, onError);
        var on = Json(onOutput);
        Assert.True(on.GetProperty("backup").GetProperty("personal").GetBoolean());
        Assert.True(on.GetProperty("changed").GetBoolean());
        Assert.True(File.Exists(Path.Combine(BackupDir, "personal", name)));

        var list = Json(Run("backup", "list", "--json").Out);
        Assert.Equal(1, list.GetProperty("count").GetInt32());
        Assert.Equal(1, list.GetProperty("personalCount").GetInt32());
        Assert.True(list.GetProperty("backups")[0].GetProperty("personal").GetBoolean());

        var (offCode, offOutput, offError) = Run("backup", "personal", name, "--off", "--json");
        Assert.True(offCode == 0, offError);
        var off = Json(offOutput);
        Assert.False(off.GetProperty("backup").GetProperty("personal").GetBoolean());
        Assert.True(off.GetProperty("changed").GetBoolean());
        Assert.True(File.Exists(Path.Combine(BackupDir, name)));
        Assert.False(Directory.Exists(Path.Combine(BackupDir, "personal", name)));
        Assert.Equal(0, Json(Run("backup", "list", "--json").Out).GetProperty("personalCount").GetInt32());

        // Exactly one of --on and --off, and an existing backup.
        Assert.Equal(1, Run("backup", "personal", name).Code);
        Assert.Equal(1, Run("backup", "personal", name, "--on", "--off").Code);
        var (missingCode, _, missingError) = Run("backup", "personal", "no such backup.zip", "--on");
        Assert.Equal(1, missingCode);
        Assert.Contains("No save backup is named", missingError);
    }

    [Fact]
    public void A_note_is_set_changed_and_cleared()
    {
        var name = NameOf(Created());

        var (setCode, setOutput, setError) = Run("backup", "note", name, "Clean start", "--json");
        Assert.True(setCode == 0, setError);
        var set = Json(setOutput);
        Assert.Equal("Clean start", set.GetProperty("backup").GetProperty("note").GetString());
        Assert.True(set.GetProperty("changed").GetBoolean());
        Assert.Equal("Clean start", Json(Run("backup", "list", "--json").Out).GetProperty("backups")[0]
            .GetProperty("note").GetString());

        var (changeCode, changeOutput, changeError) = Run("backup", "note", name, "Before the boss", "--json");
        Assert.True(changeCode == 0, changeError);
        Assert.Equal("Before the boss", Json(changeOutput).GetProperty("backup").GetProperty("note").GetString());

        var (clearCode, clearOutput, clearError) = Run("backup", "note", name, "--clear", "--json");
        Assert.True(clearCode == 0, clearError);
        var clear = Json(clearOutput);
        Assert.Equal(JsonValueKind.Null, clear.GetProperty("backup").GetProperty("note").ValueKind);
        Assert.True(clear.GetProperty("changed").GetBoolean());

        // Clearing twice changes nothing; a note needs text unless --clear, and --clear takes none.
        Assert.False(Json(Run("backup", "note", name, "--clear", "--json").Out).GetProperty("changed").GetBoolean());
        Assert.Equal(1, Run("backup", "note", name).Code);
        Assert.Equal(1, Run("backup", "note", name, "both", "--clear").Code);
    }

    [Fact]
    public void A_restore_puts_the_saves_back_only_with_yes()
    {
        var created = Created();
        var name = NameOf(created);
        var slot = Path.Combine(Saves, "Slot1.sav");
        var later = Path.Combine(Saves, "Slot2.sav");
        File.WriteAllText(slot, "slot one, broken by a mod");
        File.WriteAllText(later, "made later");

        var (previewCode, _, previewError) = Run("backup", "restore", name);
        Assert.Equal(2, previewCode);
        Assert.Contains("nothing was restored", previewError);
        Assert.Equal("slot one, broken by a mod", File.ReadAllText(slot));
        Assert.True(File.Exists(later));

        var (jsonCode, jsonOutput, _) = Run("backup", "restore", name, "--json");
        Assert.Equal(2, jsonCode);
        Assert.False(Json(jsonOutput).GetProperty("confirmed").GetBoolean());
        Assert.Equal("slot one, broken by a mod", File.ReadAllText(slot));

        var (code, output, error) = Run("backup", "restore", name, "--yes", "--json");
        Assert.True(code == 0, error);
        var restored = Json(output);
        Assert.True(restored.GetProperty("confirmed").GetBoolean());
        Assert.Equal("slot one", File.ReadAllText(slot));
        Assert.False(File.Exists(later));

        // The saves of now were backed up first, so the restore can be undone.
        var before = restored.GetProperty("beforeBackup");
        Assert.Equal(JsonValueKind.Object, before.ValueKind);
        Assert.True(File.Exists(before.GetProperty("path").GetString()!));
        Assert.Equal(2, Json(Run("backup", "list", "--json").Out).GetProperty("count").GetInt32());
    }

    [Fact]
    public void A_delete_removes_the_backup_only_with_yes()
    {
        var created = Created();
        var name = NameOf(created);
        var path = PathOf(created);

        var (previewCode, _, previewError) = Run("backup", "delete", name);
        Assert.Equal(2, previewCode);
        Assert.Contains("nothing was deleted", previewError);
        Assert.True(File.Exists(path));

        var (jsonCode, jsonOutput, _) = Run("backup", "delete", name, "--json");
        Assert.Equal(2, jsonCode);
        Assert.False(Json(jsonOutput).GetProperty("confirmed").GetBoolean());
        Assert.True(File.Exists(path));

        var (code, output, error) = Run("backup", "delete", name, "--yes", "--json");
        Assert.True(code == 0, error);
        Assert.True(Json(output).GetProperty("confirmed").GetBoolean());
        Assert.False(File.Exists(path));
        Assert.Equal(0, Json(Run("backup", "list", "--json").Out).GetProperty("count").GetInt32());
    }

    [Fact]
    public void A_missing_or_empty_save_folder_is_reported_and_no_backup_is_made()
    {
        Directory.Delete(Saves, recursive: true);

        var (missingCode, _, missingError) = Run("backup", "create", "--json");
        Assert.Equal(1, missingCode);
        var missing = Json(missingError).GetProperty("error");
        Assert.Equal("save-folder-not-found", missing.GetProperty("code").GetString());
        Assert.Contains(Path.GetFullPath(Saves), missing.GetProperty("message").GetString());
        Assert.False(Directory.Exists(BackupDir));

        Directory.CreateDirectory(Saves);
        var (emptyCode, _, emptyError) = Run("backup", "create");
        Assert.Equal(1, emptyCode);
        Assert.Contains("holds no files", emptyError);
        Assert.Equal("save-folder-empty",
            Json(Run("backup", "create", "--json").Error).GetProperty("error").GetProperty("code").GetString());
        Assert.False(Directory.Exists(BackupDir));
    }

    [Fact]
    public void An_explicit_save_folder_overrides_the_profile_one_and_is_only_a_backup_option()
    {
        var chosen = Path.Combine(root, "Chosen Saves");
        Write(Path.Combine(chosen, "Slot9.sav"), "slot nine");

        var (code, output, error) = Run("backup", "create", "--save-folder", chosen, "--json");
        Assert.True(code == 0, error);
        var created = Json(output);
        Assert.Equal(Path.GetFullPath(chosen), created.GetProperty("saveFolder").GetString());
        using (var zip = ZipFile.OpenRead(PathOf(created)))
            Assert.Equal(["Slot9.sav"], zip.Entries.Select(entry => entry.FullName.Replace('\\', '/')));

        // The option belongs to backup create and restore only.
        var (otherCode, _, otherError) = Run("list", "--save-folder", chosen);
        Assert.Equal(1, otherCode);
        Assert.Contains("--save-folder", otherError);
        Assert.Equal(1, Run("backup", "list", "--save-folder", chosen).Code);
        Assert.Equal(1, Run("list", "--note", "text").Code);
    }

    [Fact]
    public void Keep_removes_the_oldest_normal_backups()
    {
        var first = Created(keep: "2");
        File.SetLastWriteTimeUtc(PathOf(first), DateTime.UtcNow.AddMinutes(-3));
        var second = Created(keep: "2");
        File.SetLastWriteTimeUtc(PathOf(second), DateTime.UtcNow.AddMinutes(-2));
        var third = Created(keep: "2");

        Assert.StartsWith("saves ", NameOf(third));
        Assert.EndsWith(".zip", NameOf(third));
        Assert.Contains(NameOf(first), third.GetProperty("removed").EnumerateArray().Select(name => name.GetString()!).ToList());
        Assert.Equal(2, Json(Run("backup", "list", "--json").Out).GetProperty("count").GetInt32());
        Assert.False(File.Exists(PathOf(first)));
        Assert.True(File.Exists(PathOf(second)));
        Assert.True(File.Exists(PathOf(third)));

        // The count is a whole number of at least 1.
        Assert.Equal(1, Run("backup", "create", "--keep", "0").Code);
        Assert.Equal(1, Run("backup", "create", "--keep", "many").Code);
    }

    [Fact]
    public void A_backup_write_waits_while_the_app_holds_the_data_folder()
    {
        var created = Created();
        var name = NameOf(created);
        var path = PathOf(created);

        // The app holds this lock for as long as it runs.
        using (var app = DataFolderLock.TryTake(new AppPaths(Data)))
        {
            Assert.NotNull(app);
            var (createCode, _, createError) = Run("backup", "create", "--json");
            Assert.Equal(1, createCode);
            Assert.Contains("UE MM is open", createError);

            var (deleteCode, _, deleteError) = Run("backup", "delete", name, "--yes", "--json");
            Assert.Equal(1, deleteCode);
            Assert.Contains("UE MM is open", deleteError);

            var (noteCode, _, noteError) = Run("backup", "note", name, "text", "--json");
            Assert.Equal(1, noteCode);
            Assert.Contains("UE MM is open", noteError);

            // Listing writes nothing, so it still works while the app is open.
            var (listCode, listOutput, _) = Run("backup", "list", "--json");
            Assert.Equal(0, listCode);
            Assert.Equal(1, Json(listOutput).GetProperty("count").GetInt32());
            Assert.True(File.Exists(path));
        }

        var (afterCode, afterOutput, afterError) = Run("backup", "delete", name, "--yes", "--json");
        Assert.True(afterCode == 0, afterError);
        Assert.True(Json(afterOutput).GetProperty("confirmed").GetBoolean());
    }

    [Fact]
    public void A_mistyped_backup_command_says_what_it_needs()
    {
        var (noneCode, _, noneError) = Run("backup");
        Assert.Equal(1, noneCode);
        Assert.Contains("subcommand", noneError);

        Assert.Equal(1, Run("backup", "explode").Code);

        var (restoreCode, _, restoreError) = Run("backup", "restore");
        Assert.Equal(1, restoreCode);
        Assert.Contains("needs a backup's name", restoreError);

        Assert.Equal(1, Run("backup", "list", "extra").Code);
        Assert.Equal(1, Run("backup", "delete", "a.zip", "b.zip").Code);

        var (emptyCode, emptyOutput, emptyError) = Run("backup", "list", "--json");
        Assert.True(emptyCode == 0, emptyError);
        Assert.Equal(0, Json(emptyOutput).GetProperty("count").GetInt32());
    }
}
