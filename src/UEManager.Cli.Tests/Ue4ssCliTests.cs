using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using UEManager.Cli;
using UEManager.Core.Mods;
using UEManager.Core.Storage;

namespace UEManager.Cli.Tests;

/// <summary>
/// The `ue4ss` commands, run in this process against a fake Phantom Blade Zero install and a data folder of its own.
/// Every test is offline: the manifest checks point at a loopback address that refuses the connection.
/// </summary>
[Collection(CliConsole.Name)]
public sealed class Ue4ssCliTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "uemm-cli-ue4ss-tests", Guid.NewGuid().ToString("N"));

    private string Data => Path.Combine(root, "Data");

    private string GameRoot => Path.Combine(root, "Game");

    /// <summary>Where UE4SS goes for Phantom Blade Zero: the folder of the store's exe, platform\steam.</summary>
    private string Win64 => Path.Combine(GameRoot, "platform", "steam");

    private string Ue4ssFolder => Path.Combine(Win64, "ue4ss");

    private string RecordFile => Path.Combine(Data, "games", "phantom-blade-zero", "ue4ss.json");

    public Ue4ssCliTests()
    {
        // What the game's profile needs to know an install: the Engine folder, the project folder and the exe.
        Directory.CreateDirectory(Path.Combine(GameRoot, "Engine"));
        Directory.CreateDirectory(Path.Combine(GameRoot, "ProjectPBZ", "Content", "Paks"));
        Write(Path.Combine(Win64, "PBZ.exe"), "the game");
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

    /// <summary>A release zip laid out like UE4SS 3.x: dwmapi.dll and the ue4ss folder, with a version in its name.</summary>
    private string ReleaseZip(string version, string runtime = "runtime")
    {
        var source = Path.Combine(root, "Release", "ue4ss_" + version);
        Write(Path.Combine(source, "dwmapi.dll"), "proxy " + version);
        Write(Path.Combine(source, "ue4ss", "UE4SS.dll"), runtime + " " + version);
        var zip = source + ".zip";
        ZipFile.CreateFromDirectory(source, zip, CompressionLevel.Fastest, includeBaseDirectory: true);
        return zip;
    }

    /// <summary>A download holding a Lua mod that lives in UE4SS's Mods folder, as the app installs such mods.</summary>
    private string LuaMod()
    {
        var folder = Path.Combine(root, "Downloads", "DashTest");
        Write(Path.Combine(folder, "ue4ss", "Mods", "DashTest", "Scripts", "main.lua"), "print('dash')");
        return folder;
    }

    private void InstallUe4ss(string version = "v3.0.1-7-gabc")
    {
        var (code, _, error) = Run("ue4ss", "install", "--package", ReleaseZip(version), "--version", version);
        Assert.True(code == 0, error);
    }

    [Fact]
    public void The_help_lists_the_ue4ss_commands()
    {
        var (code, output, _) = Raw("--help");

        Assert.Equal(0, code);
        Assert.Contains("ue4ss status", output);
        Assert.Contains("ue4ss install", output);
        Assert.Contains("ue4ss update", output);
        Assert.Contains("ue4ss remove", output);
    }

    [Fact]
    public void Status_without_ue4ss_reads_nothing_online()
    {
        var (code, output, error) = Run("ue4ss", "status", "--json");

        Assert.Equal(0, code);
        Assert.Empty(error);
        Assert.Contains("\"installed\": false", output);
        Assert.Contains("\"recordExists\": false", output);
        Assert.Contains("\"updateChecked\": false", output);
        Assert.Contains("\"latestVersion\": null", output);
    }

    [Fact]
    public void Status_reports_the_record_and_the_ue4ss_files()
    {
        Write(Path.Combine(Ue4ssFolder, "UE4SS.dll"), "runtime");
        JsonFile.Write(RecordFile, new Ue4ssInstallRecord
        {
            Version = "v3.0.1-1181-g83330635",
            Source = "pbz_ue4ss.zip",
            InstalledAt = new DateTimeOffset(2026, 11, 15, 3, 0, 0, TimeSpan.FromHours(8)),
            Files = new Dictionary<string, string> { ["ue4ss/UE4SS.dll"] = "abc" },
        });

        var (code, output, _) = Run("ue4ss", "status", "--json");

        Assert.Equal(0, code);
        Assert.Contains("\"installed\": true", output);
        Assert.Contains("\"version\": \"v3.0.1-1181-g83330635\"", output);
        Assert.Contains("\"versionSource\": \"record\"", output);
        Assert.Contains("\"managed\": true", output);
        Assert.Contains("\"source\": \"pbz_ue4ss.zip\"", output);
        Assert.Contains("\"installedAt\": \"2026-11-15T03:00:00+08:00\"", output);
        Assert.Contains("\"filesRecorded\": 1", output);
    }

    [Fact]
    public void Install_from_a_package_writes_the_files_and_the_record_and_installs_again_as_an_update()
    {
        var first = Run("ue4ss", "install", "--package", ReleaseZip("v3.0.1-7-gabc"), "--version", "v3.0.1-7-gabc", "--json");

        Assert.Equal(0, first.Code);
        Assert.Contains("\"action\": \"installed\"", first.Out);
        Assert.True(File.Exists(Path.Combine(Win64, "dwmapi.dll")));
        Assert.True(File.Exists(Path.Combine(Ue4ssFolder, "UE4SS.dll")));
        var record = JsonFile.Read<Ue4ssInstallRecord>(RecordFile)!;
        Assert.Equal("v3.0.1-7-gabc", record.Version);
        Assert.Contains("ue4ss/UE4SS.dll", record.Files!.Keys);
        Assert.Contains("dwmapi.dll", record.Files.Keys);

        Assert.Contains("\"installed\": true", Run("ue4ss", "status", "--json").Out);

        // The version comes from the package's file name here.
        var second = Run("ue4ss", "install", "--package", ReleaseZip("v3.0.2-9-gdef"), "--json");

        Assert.Equal(0, second.Code);
        Assert.Contains("\"action\": \"updated\"", second.Out);
        Assert.Contains("\"updated\": true", second.Out);
        Assert.Equal("v3.0.2-9-gdef", JsonFile.Read<Ue4ssInstallRecord>(RecordFile)!.Version);
        // Nothing of the download stays behind: neither the data folder's staging nor the package.
        var staging = Path.Combine(Data, "staging");
        Assert.False(Directory.Exists(staging) && Directory.EnumerateFileSystemEntries(staging).Any());
    }

    [Fact]
    public void Update_says_why_it_cannot_check_without_a_manifest_and_changes_nothing()
    {
        Write(Path.Combine(Ue4ssFolder, "UE4SS.dll"), "runtime");

        // A loopback address nothing listens on: no internet, and the failure arrives at once.
        var (code, _, error) = Run("ue4ss", "update", "--manifest-url", "http://127.0.0.1:1/manifest.json", "--json");

        Assert.Equal(1, code);
        Assert.Contains("manifest", error);
        Assert.Contains("\"code\": \"manifest-unreachable\"", error);
        Assert.False(File.Exists(Path.Combine(Win64, "dwmapi.dll")));
    }

    [Fact]
    public void Update_without_ue4ss_says_so_instead_of_installing()
    {
        var (code, _, error) = Run("ue4ss", "update", "--manifest-url", "http://127.0.0.1:1/manifest.json");

        Assert.Equal(1, code);
        Assert.Contains("not installed", error);
        Assert.False(Directory.Exists(Ue4ssFolder));
    }

    [Fact]
    public void Remove_needs_yes_and_then_takes_the_files_and_disables_the_mods_in_them()
    {
        InstallUe4ss();
        var mod = Run("install", LuaMod(), "--yes");
        Assert.Equal(0, mod.Code);
        Assert.True(File.Exists(Path.Combine(Ue4ssFolder, "Mods", "DashTest", "Scripts", "main.lua")));

        var preview = Run("ue4ss", "remove", "--json");

        Assert.Equal(2, preview.Code);
        Assert.Contains("\"confirmed\": false", preview.Out);
        Assert.Contains("DashTest", preview.Out);
        Assert.Contains("nothing was removed", preview.Error);
        Assert.True(File.Exists(Path.Combine(Ue4ssFolder, "UE4SS.dll")));
        Assert.True(File.Exists(Path.Combine(Win64, "dwmapi.dll")));

        var removed = Run("ue4ss", "remove", "--yes", "--json");

        Assert.Equal(0, removed.Code);
        Assert.Contains("\"confirmed\": true", removed.Out);
        Assert.Contains("DashTest", removed.Out);
        Assert.False(Directory.Exists(Ue4ssFolder));
        Assert.False(File.Exists(Path.Combine(Win64, "dwmapi.dll")));
        Assert.False(File.Exists(RecordFile));
        // The mod stays in the list, disabled, and its library copy stays, as the app leaves it.
        var list = Run("list", "--json");
        Assert.Contains("DashTest", list.Out);
        Assert.Contains("\"enabled\": false", list.Out);
    }

    [Fact]
    public void A_change_waits_while_the_app_holds_the_data_folder_but_status_does_not()
    {
        var zip = ReleaseZip("v3.0.1-7-gabc");
        using (var app = DataFolderLock.TryTake(new AppPaths(Data)))
        {
            Assert.NotNull(app);

            var (code, _, error) = Run("ue4ss", "install", "--package", zip);

            Assert.Equal(1, code);
            Assert.Contains("UE MM is open", error);
            Assert.False(Directory.Exists(Ue4ssFolder));

            // Reading the game and the record needs no lock, so status works beside the open app.
            Assert.Equal(0, Run("ue4ss", "status", "--json").Code);
        }

        Assert.Equal(0, Run("ue4ss", "install", "--package", zip).Code);
        Assert.True(File.Exists(Path.Combine(Ue4ssFolder, "UE4SS.dll")));
    }

    [Fact]
    public void Install_reads_the_manifest_checks_the_checksum_and_update_then_says_it_is_up_to_date()
    {
        var zip = File.ReadAllBytes(ReleaseZip("v3.0.2-9-gdef"));
        using var server = new Loopback();
        server.Add("/manifest.json", Manifest("v3.0.2-9-gdef", "ue4ss.zip", Sha256(zip)));
        server.Add("/ue4ss.zip", zip);

        var install = Run("ue4ss", "install", "--manifest-url", server.Url("/manifest.json"), "--json");

        Assert.Equal(0, install.Code);
        Assert.Contains("\"action\": \"installed\"", install.Out);
        Assert.Contains("\"version\": \"v3.0.2-9-gdef\"", install.Out);
        Assert.Equal("v3.0.2-9-gdef", JsonFile.Read<Ue4ssInstallRecord>(RecordFile)!.Version);
        Assert.True(File.Exists(Path.Combine(Ue4ssFolder, "UE4SS.dll")));
        Assert.Contains("/ue4ss.zip", server.Requests);
        // Nothing of the download stays behind: neither the data folder's staging nor the package.
        Assert.False(Directory.Exists(Path.Combine(Data, "staging")) && Directory.EnumerateFileSystemEntries(Path.Combine(Data, "staging")).Any());

        // The record and the manifest name the same release, so an update downloads nothing.
        var requestsBefore = server.Requests.Count;
        var update = Run("ue4ss", "update", "--manifest-url", server.Url("/manifest.json"), "--json");

        Assert.Equal(0, update.Code);
        Assert.Contains("\"action\": \"up-to-date\"", update.Out);
        Assert.Equal(requestsBefore + 1, server.Requests.Count);

        var status = Run("ue4ss", "status", "--check", "--manifest-url", server.Url("/manifest.json"), "--json");

        Assert.Equal(0, status.Code);
        Assert.Contains("\"updateChecked\": true", status.Out);
        Assert.Contains("\"updateAvailable\": false", status.Out);
        Assert.Contains("\"latestVersion\": \"v3.0.2-9-gdef\"", status.Out);
    }

    [Fact]
    public void A_download_whose_checksum_differs_is_refused_and_its_temporary_folder_goes()
    {
        var zip = File.ReadAllBytes(ReleaseZip("v3.0.2-9-gdef"));
        using var server = new Loopback();
        server.Add("/ue4ss.zip", zip);
        var release = new Ue4ssRelease("v3.0.2-9-gdef", server.Url("/ue4ss.zip"), new string('0', 64));
        var staging = Path.Combine(root, "Staging");

        var error = Assert.Throws<CliError>(() => Ue4ssPackage.Download(release, staging));

        Assert.Equal("download-failed", error.Code);
        Assert.Equal(1, error.ExitCode);
        Assert.Contains("checksum", error.Message);
        Assert.False(Directory.Exists(staging) && Directory.EnumerateFileSystemEntries(staging).Any());
    }

    [Fact]
    public void A_release_that_is_not_ue4ss_is_refused_and_leaves_nothing_in_the_game()
    {
        // A valid zip that is not a UE4SS release: the download succeeds and the Core refuses it.
        var source = Path.Combine(root, "Release", "NotUe4ss");
        Write(Path.Combine(source, "readme.txt"), "nothing to see");
        var zipPath = source + ".zip";
        ZipFile.CreateFromDirectory(source, zipPath, CompressionLevel.Fastest, includeBaseDirectory: true);
        var zip = File.ReadAllBytes(zipPath);
        using var server = new Loopback();
        server.Add("/manifest.json", Manifest("v3.0.2-9-gdef", "ue4ss.zip", Sha256(zip)));
        server.Add("/ue4ss.zip", zip);

        var (code, _, error) = Run("ue4ss", "install", "--manifest-url", server.Url("/manifest.json"), "--json");

        Assert.Equal(1, code);
        Assert.Contains("not a UE4SS release", error);
        Assert.False(Directory.Exists(Ue4ssFolder));
        Assert.False(File.Exists(Path.Combine(Win64, "dwmapi.dll")));
        Assert.False(File.Exists(RecordFile));
        Assert.False(Directory.Exists(Path.Combine(Data, "staging")) && Directory.EnumerateFileSystemEntries(Path.Combine(Data, "staging")).Any());
    }

    /// <summary>The manifest a game profile's addresses serve: a Cloudflare-style mirror and the GitHub release.</summary>
    private static byte[] Manifest(string version, string mirror, string sha256) => Encoding.UTF8.GetBytes($$"""
        { "ue4ss": { "phantom-blade-zero": {
          "version": "{{version}}",
          "url": "https://github.com/example/ue4ss/releases/download/{{version}}/ue4ss_{{version}}.zip",
          "sha256": "{{sha256}}",
          "mirror": "{{mirror}}" } } }
        """);

    private static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    /// <summary>
    /// A tiny HTTP server on the loopback interface: the manifest and its release file, so the online commands run
    /// end to end without the internet. It answers one request per connection ("Connection: close").
    /// </summary>
    private sealed class Loopback : IDisposable
    {
        private readonly TcpListener listener = new(IPAddress.Loopback, 0);
        private readonly Dictionary<string, byte[]> routes = new(StringComparer.OrdinalIgnoreCase);
        private readonly CancellationTokenSource stopping = new();
        private readonly Task serving;

        public Loopback()
        {
            listener.Start();
            serving = Task.Run(ServeAsync);
        }

        /// <summary>The paths asked for, in order: a test can tell that no download happened.</summary>
        public List<string> Requests { get; } = [];

        private int Port => ((IPEndPoint)listener.LocalEndpoint).Port;

        public string Url(string path) => $"http://127.0.0.1:{Port}{path}";

        public void Add(string path, byte[] body) => routes[path] = body;

        private async Task ServeAsync()
        {
            while (!stopping.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await listener.AcceptTcpClientAsync(stopping.Token);
                }
                catch (Exception error) when (error is OperationCanceledException or ObjectDisposedException or SocketException)
                {
                    return;
                }
                _ = Task.Run(() => AnswerAsync(client));
            }
        }

        private async Task AnswerAsync(TcpClient client)
        {
            using (client)
            {
                var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
                var request = await reader.ReadLineAsync() ?? "";
                while (!string.IsNullOrEmpty(await reader.ReadLineAsync()))
                {
                    // The headers end with an empty line; a GET has no body.
                }
                var path = request.Split(' ') is [_, var asked, ..] ? asked : "";
                var query = path.IndexOf('?');
                if (query >= 0)
                    path = path[..query];
                lock (Requests)
                    Requests.Add(path);
                string status;
                byte[] body;
                if (routes.TryGetValue(path, out var found))
                {
                    status = "200 OK";
                    body = found;
                }
                else
                {
                    status = "404 Not Found";
                    body = [];
                }
                var head = Encoding.ASCII.GetBytes($"HTTP/1.1 {status}\r\nContent-Length: {body.Length}\r\n"
                    + $"Content-Type: application/octet-stream\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(head);
                await stream.WriteAsync(body);
                await stream.FlushAsync();
            }
        }

        public void Dispose()
        {
            stopping.Cancel();
            listener.Stop();
            try
            {
                serving.Wait(TimeSpan.FromSeconds(2));
            }
            catch (AggregateException)
            {
                // The server was stopped while waiting for a connection.
            }
        }
    }
}
