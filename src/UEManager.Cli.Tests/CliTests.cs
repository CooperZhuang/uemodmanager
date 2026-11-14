using System.IO.Compression;
using System.Text.Json;
using UEManager.Cli;
using UEManager.Core.Storage;

namespace UEManager.Cli.Tests;

/// <summary>
/// The command line tool, run in this process against a fake Phantom Blade Zero install and a data folder of its own.
/// One class, so the tests run one after the other: they take over the console's output.
/// </summary>
public sealed class CliTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "uemm-cli-tests", Guid.NewGuid().ToString("N"));

    private string Data => Path.Combine(root, "Data");

    private string GameRoot => Path.Combine(root, "Game");

    private string ModsFolder => Path.Combine(GameRoot, "ProjectPBZ", "Content", "Paks", "~mods");

    public CliTests()
    {
        // What the game's profile needs to know an install: the Engine folder, the project folder and the exe.
        Directory.CreateDirectory(Path.Combine(GameRoot, "Engine"));
        Directory.CreateDirectory(Path.Combine(GameRoot, "ProjectPBZ", "Content", "Paks"));
        Write(Path.Combine(GameRoot, "platform", "steam", "PBZ.exe"), "the game");
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
            // The log file may still be open; the temp folder is cleaned by Windows.
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

    private string Download()
    {
        var folder = Path.Combine(root, "Downloads", "MyMod");
        Write(Path.Combine(folder, "MyMod_P.pak"), "a pak");
        return folder;
    }

    /// <summary>A zip download at <paramref name="fileName"/>, with the given files below its top.</summary>
    private string Zip(string fileName, params (string Path, string Text)[] files)
    {
        var path = Path.Combine(root, "Downloads", fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (name, text) in files)
        {
            var entry = archive.CreateEntry(name.Replace('\\', '/'));
            using var writer = new StreamWriter(entry.Open());
            writer.Write(text);
        }
        return path;
    }

    [Fact]
    public void A_change_waits_while_the_app_holds_the_data_folder()
    {
        // The app holds this lock for as long as it runs.
        using (var app = DataFolderLock.TryTake(new AppPaths(Data)))
        {
            Assert.NotNull(app);
            var (code, _, error) = Run("install", Download(), "--yes");

            Assert.Equal(1, code);
            Assert.Contains("UE MM is open", error);
            Assert.False(Directory.Exists(ModsFolder));
        }

        // Once the app is closed, the same command works, and gives the folder free again at its end.
        Assert.Equal(0, Run("install", Download(), "--yes").Code);
        using var again = DataFolderLock.TryTake(new AppPaths(Data));
        Assert.NotNull(again);
    }

    [Fact]
    public void Without_a_data_folder_it_says_where_one_belongs()
    {
        var (code, _, error) = Raw("list");

        Assert.Equal(1, code);
        Assert.Contains("--data-dir", error);
        Assert.Contains("UEModManager.exe", error);
        Assert.DoesNotContain(@"C:\Games", error);
    }

    [Fact]
    public void Updates_is_no_command_any_more()
    {
        // It read files the app never writes: the app keeps the mod pages' versions in memory only.
        var (code, _, error) = Run("updates");

        Assert.Equal(1, code);
        Assert.Contains("Unknown command", error);
    }

    [Fact]
    public void A_mod_installs_after_its_plan_and_turns_off_on_and_goes()
    {
        var pak = Path.Combine(ModsFolder, "MyMod_P.pak");

        // Without --yes the plan only: exit code 2, nothing in the game.
        var plan = Run("install", Download());
        Assert.Equal(2, plan.Code);
        Assert.Contains("\"canInstall\": true", plan.Out);
        Assert.False(File.Exists(pak));

        Assert.Equal(0, Run("install", Download(), "--yes").Code);
        Assert.True(File.Exists(pak));
        Assert.Contains("MyMod", Run("list", "--json").Out);

        Assert.Equal(0, Run("disable", "MyMod").Code);
        Assert.False(File.Exists(pak));
        Assert.Equal(0, Run("enable", "MyMod").Code);
        Assert.True(File.Exists(pak));

        Assert.Equal(2, Run("uninstall", "MyMod").Code);
        Assert.True(File.Exists(pak));
        Assert.Equal(0, Run("uninstall", "MyMod", "--yes").Code);
        Assert.False(File.Exists(pak));
        Assert.Contains("0 mod(s)", Run("list").Out);
    }

    [Fact]
    public void A_download_from_the_same_page_installs_beside_the_mod_with_keep_both()
    {
        // The same mod page, twice: both downloads hold MyMod_P.pak, the second is a newer file of that page.
        var first = Zip("MyMod-77-1-0-1730000000.zip", ("MyMod_P.pak", "the first pak"));
        var second = Zip("MyMod-77-2-0-1730000001.zip", ("MyMod_P.pak", "the second pak"));
        var firstPak = Path.Combine(ModsFolder, "MyMod_P.pak");

        Assert.Equal(0, Run("install", first, "--yes").Code);
        Assert.Equal("the first pak", File.ReadAllText(firstPak));

        // Without --keep-both the plan would replace the installed mod.
        var replace = Run("install", second);
        Assert.Equal(2, replace.Code);
        Assert.Contains("\"replaces\": {", replace.Out);

        // With --keep-both the plan replaces nothing and gives the new mod a folder of its own below ~mods.
        var plan = Run("install", second, "--keep-both");
        Assert.Equal(2, plan.Code);
        Assert.Contains("\"keepBoth\": true", plan.Out);
        Assert.Contains("\"ownFolder\": \"MyMod\"", plan.Out);
        Assert.Contains("\"replaces\": null", plan.Out);

        Assert.Equal(0, Run("install", second, "--keep-both", "--yes").Code);

        // Two mods, the first file untouched, the second in its own folder.
        var secondPak = Path.Combine(ModsFolder, "MyMod", "MyMod_P.pak");
        Assert.Equal("the first pak", File.ReadAllText(firstPak));
        Assert.Equal("the second pak", File.ReadAllText(secondPak));
        using var list = JsonDocument.Parse(Run("list", "--json").Out);
        Assert.Equal(2, list.RootElement.GetProperty("count").GetInt32());
    }

    [Fact]
    public void Part_picks_the_named_parts_of_a_download()
    {
        var download = Zip("TwoParts-88-1-0-1730000002.zip", ("A_P.pak", "a pak"), ("B_P.pak", "b pak"));
        var a = Path.Combine(ModsFolder, "A_P.pak");
        var b = Path.Combine(ModsFolder, "B_P.pak");

        // The default selection takes both parts, and the plan numbers them from 1.
        using (var plan = JsonDocument.Parse(Run("plan", download).Out))
        {
            var parts = plan.RootElement.GetProperty("parts");
            Assert.Equal(2, parts.GetArrayLength());
            Assert.Equal(new[] { 1, 2 }, Enumerable.Range(0, 2).Select(i => parts[i].GetProperty("index").GetInt32()));
            Assert.All(Enumerable.Range(0, 2), i => Assert.True(parts[i].GetProperty("selected").GetBoolean()));
        }

        // --part 2 takes only the second one.
        using (var plan = JsonDocument.Parse(Run("plan", download, "--part", "2").Out))
        {
            var parts = plan.RootElement.GetProperty("parts");
            Assert.False(parts[0].GetProperty("selected").GetBoolean());
            Assert.True(parts[1].GetProperty("selected").GetBoolean());
        }

        // A part's name works instead of its index.
        Assert.Equal(0, Run("install", download, "--part", "B_P", "--yes").Code);
        Assert.False(File.Exists(a));
        Assert.True(File.Exists(b));
    }

    [Fact]
    public void A_part_that_is_not_there_lists_the_ones_that_are()
    {
        var download = Zip("TwoParts-88-1-0-1730000002.zip", ("A_P.pak", "a pak"), ("B_P.pak", "b pak"));

        var (code, _, error) = Run("plan", download, "--part", "9");

        Assert.Equal(1, code);
        Assert.Contains("No part matches '9'", error);
        Assert.Contains("Available parts:", error);
        Assert.Contains("A_P", error);
        Assert.Contains("B_P", error);
    }

    [Fact]
    public void Part_and_all_parts_together_are_an_argument_error()
    {
        var download = Zip("TwoParts-88-1-0-1730000002.zip", ("A_P.pak", "a pak"), ("B_P.pak", "b pak"));

        var (code, _, error) = Run("plan", download, "--part", "1", "--all-parts");

        Assert.Equal(1, code);
        Assert.Contains("--part", error);
        Assert.Contains("--all-parts", error);
        Assert.False(Directory.Exists(ModsFolder));
    }

    [Fact]
    public void A_part_that_needs_a_missing_loader_is_a_problem()
    {
        var download = Zip("MenuMod-90-1-0-1730000003.zip",
            ("MyMod_P.pak", "a pak"),
            ("ProjectPBZ/Content/JavaScript/PBZModsMenu/Mods/MenuThing/main.js", "// a Mods Menu script mod"));

        // The pak alone installs; the script mod needs Mods Menu, which this install does not have.
        using (var plan = JsonDocument.Parse(Run("plan", download, "--part", "1").Out))
            Assert.True(plan.RootElement.GetProperty("canInstall").GetBoolean());
        using (var plan = JsonDocument.Parse(Run("plan", download, "--part", "2").Out))
        {
            Assert.False(plan.RootElement.GetProperty("canInstall").GetBoolean());
            var problems = string.Join(" | ", plan.RootElement.GetProperty("problems").EnumerateArray().Select(p => p.GetString()));
            Assert.Contains("Mods Menu", problems);
        }
    }

    [Fact]
    public void Staging_keeps_the_unpacked_download_out_of_the_data_folder()
    {
        var download = Download();
        var staging = Path.Combine(root, "SharedStaging");

        var (code, output, _) = Run("plan", download, "--staging", staging);

        Assert.Equal(0, code);
        Assert.Contains("\"canInstall\": true", output);
        // The download was unpacked below the given folder and cleaned up again; the data folder kept no staging.
        Assert.True(Directory.Exists(staging));
        Assert.Empty(Directory.EnumerateFileSystemEntries(staging));
        Assert.False(Directory.Exists(Path.Combine(Data, "staging")));
    }
}
