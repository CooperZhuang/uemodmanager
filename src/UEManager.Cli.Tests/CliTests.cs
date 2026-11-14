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
}
