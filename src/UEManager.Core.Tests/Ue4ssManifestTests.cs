using System.Net;
using System.Text;
using UEManager.Core.Games;
using UEManager.Core.Mods;
using UEManager.Core.Storage;
using UEManager.Core.Updates;

namespace UEManager.Core.Tests;

public class Ue4ssManifestTests : IDisposable
{
    private const string Manifest = """
        {
          "ue4ss": {
            "mortal-shell-2": {
              "version": "v3.0.1-1181-g83330635",
              "url": "https://github.com/KintaroModding/ue4ss/releases/download/v3.0.1-1181-g83330635/mortalshell2_ue4ss_v3.0.1-1181-g83330635.zip",
              "sha256": "A155C673506D34AA78E4C20579E39DAB35F62CB68EA04D4263E5EAE39066AC4A"
            },
            "plain-http": { "version": "v1", "url": "http://example.test/ue4ss.zip" }
          }
        }
        """;

    private readonly TestFolder folder = new();
    private readonly GameInstall game;
    private readonly ModManager manager;

    public Ue4ssManifestTests()
    {
        game = GameLocator.Resolve(TestFolder.Profile(), folder.Game(), GameStore.Manual)!;
        manager = new ModManager(game, new AppPaths(folder.Combine("Data")));
    }

    public void Dispose() => folder.Dispose();

    private string Win64 => game.BinariesPath;

    [Fact]
    public void Reads_the_entry_for_the_game()
    {
        var release = Ue4ssManifest.Parse(Manifest, "mortal-shell-2");

        Assert.NotNull(release);
        Assert.Equal("v3.0.1-1181-g83330635", release.Version);
        Assert.Equal("mortalshell2_ue4ss_v3.0.1-1181-g83330635.zip", release.FileName);
        Assert.StartsWith("A155C673", release.Sha256);
    }

    [Theory]
    [InlineData("phantom-blade-zero")]
    [InlineData("plain-http")]
    public void Missing_or_unsafe_entries_are_ignored(string gameId)
    {
        Assert.Null(Ue4ssManifest.Parse(Manifest, gameId));
    }

    [Fact]
    public async Task Fetching_returns_the_entry_and_fails_quietly()
    {
        using var ok = new HttpClient(new FixedResponse(HttpStatusCode.OK, Manifest));
        using var missing = new HttpClient(new FixedResponse(HttpStatusCode.NotFound, ""));
        using var broken = new HttpClient(new FixedResponse(HttpStatusCode.OK, "{ not json"));

        Assert.NotNull(await Ue4ssManifest.FetchAsync(ok, "https://example.test/manifest.json", "mortal-shell-2"));
        Assert.Null(await Ue4ssManifest.FetchAsync(missing, "https://example.test/manifest.json", "mortal-shell-2"));
        Assert.Null(await Ue4ssManifest.FetchAsync(broken, "https://example.test/manifest.json", "mortal-shell-2"));
    }

    [Fact]
    public void A_mirror_next_to_the_manifest_comes_first_and_GitHub_stays_the_fallback()
    {
        const string json = """
            { "ue4ss": { "phantom-blade-zero": {
                "version": "v3.0.1-1195-g1ceefac8",
                "url": "https://github.com/KintaroModding/ue4ss/releases/download/v3.0.1-1195-g1ceefac8/phantombladezero_ue4ss_v3.0.1-1195-g1ceefac8.zip",
                "sha256": "abc",
                "mirror": "ue4ss/phantombladezero_ue4ss_v3.0.1-1195-g1ceefac8.zip" } } }
            """;

        var release = Ue4ssManifest.Parse(json, "phantom-blade-zero", new Uri("https://metadata.openmods.nexus/uemodmanager/manifest.json"))!;

        Assert.Equal(
            ["https://metadata.openmods.nexus/uemodmanager/ue4ss/phantombladezero_ue4ss_v3.0.1-1195-g1ceefac8.zip",
             "https://github.com/KintaroModding/ue4ss/releases/download/v3.0.1-1195-g1ceefac8/phantombladezero_ue4ss_v3.0.1-1195-g1ceefac8.zip"],
            release.Urls);
        Assert.Equal("phantombladezero_ue4ss_v3.0.1-1195-g1ceefac8.zip", release.FileName);
        // Without the manifest's address, as for a manifest read from a file, only GitHub is left.
        Assert.Single(Ue4ssManifest.Parse(json, "phantom-blade-zero")!.Urls);
        // A plain http mirror counts only for a test manifest served over http.
        Assert.Single(Ue4ssManifest.Parse(json.Replace("\"ue4ss/", "\"http://example.test/"), "phantom-blade-zero",
            new Uri("https://metadata.openmods.nexus/uemodmanager/manifest.json"))!.Urls);
    }

    [Fact]
    public void The_manifest_names_the_recommended_mod_of_a_game()
    {
        const string json = """
            { "recommended": {
                "phantom-blade-zero": { "repo": "KintaroModding/pbz_fix", "name": " The Fix ", "folder": "PBZFix",
                  "text": { "en": "Fixes it.", "zh-CN": "修复。", "bad": 3 } },
                "experimental-game": { "repo": "a/b", "name": "x", "experimental": true,
                  "notes": { "en": "How it works.", "zh-CN": "原理。", "bad": 3 } },
                "no-repo": { "name": "x" },
                "odd-repo": { "repo": "../../etc", "name": "x" },
                "no-name": { "repo": "a/b" } } }
            """;

        var mod = Ue4ssManifest.ParseRecommended(json, "phantom-blade-zero")!;

        Assert.Equal("The Fix", mod.Name);
        Assert.Equal("pbz_fix", mod.RepoName);
        Assert.Equal("PBZFix", mod.Folder);
        Assert.Equal("修复。", mod.TextIn("zh-CN"));
        Assert.Equal("Fixes it.", mod.TextIn("vi"));
        Assert.False(mod.Experimental);
        Assert.Null(mod.NotesIn("en"));
        var experimental = Ue4ssManifest.ParseRecommended(json, "experimental-game")!;
        Assert.True(experimental.Experimental);
        Assert.Equal("原理。", experimental.NotesIn("zh-CN"));
        Assert.Equal("How it works.", experimental.NotesIn("vi"));
        Assert.Null(Ue4ssManifest.ParseRecommended(json, "no-repo"));
        Assert.Null(Ue4ssManifest.ParseRecommended(json, "odd-repo"));
        Assert.Null(Ue4ssManifest.ParseRecommended(json, "no-name"));
        Assert.Null(Ue4ssManifest.ParseRecommended(json, "another-game"));
        Assert.Null(Ue4ssManifest.ParseRecommended(Manifest, "phantom-blade-zero"));
    }

    [Fact]
    public async Task Several_addresses_are_tried_in_order_until_one_answers()
    {
        var asked = new List<string>();
        using var http = new HttpClient(new ByAddress(asked, new()
        {
            ["https://first.test/manifest.json"] = (HttpStatusCode.NotFound, ""),
            ["https://second.test/manifest.json"] = (HttpStatusCode.OK, Manifest),
            ["https://third.test/manifest.json"] = (HttpStatusCode.OK, Manifest),
        }));

        var answer = await Ue4ssManifest.FetchAsync(http,
            ["https://first.test/manifest.json", "https://second.test/manifest.json", "https://third.test/manifest.json"], "mortal-shell-2");

        Assert.Equal("https://second.test/manifest.json", answer?.Url);
        Assert.Equal("v3.0.1-1181-g83330635", answer?.Release?.Version);
        Assert.Equal(["https://first.test/manifest.json", "https://second.test/manifest.json"], asked);
    }

    [Fact]
    public async Task An_answer_without_the_games_entry_still_counts()
    {
        var asked = new List<string>();
        using var http = new HttpClient(new ByAddress(asked, new()
        {
            ["https://first.test/manifest.json"] = (HttpStatusCode.OK, Manifest),
            ["https://second.test/manifest.json"] = (HttpStatusCode.OK, Manifest),
        }));

        var answer = await Ue4ssManifest.FetchAsync(http, ["https://first.test/manifest.json", "https://second.test/manifest.json"], "phantom-blade-zero");

        Assert.Equal("https://first.test/manifest.json", answer?.Url);
        Assert.Null(answer?.Release);
        Assert.Single(asked);
    }

    [Theory]
    [InlineData("https://metadata.openmods.nexus/uemodmanager/manifest.json", RemoteSource.Cloudflare)]
    [InlineData("https://uemm-metadata.pages.dev/uemodmanager/manifest.json", RemoteSource.Cloudflare)]
    [InlineData("https://raw.githubusercontent.com/KintaroModding/metadata/main/uemodmanager/manifest.json", RemoteSource.GitHub)]
    [InlineData("http://127.0.0.1:8765/manifest.json", RemoteSource.Other)]
    [InlineData("https://notgithub.com.example/manifest.json", RemoteSource.Other)]
    public void The_source_of_an_answer_is_named_by_its_host(string url, RemoteSource source) =>
        Assert.Equal(source, RemoteSources.Of(url));

    [Fact]
    public async Task A_slow_address_is_given_up_after_its_time()
    {
        using var http = new HttpClient(new SlowFirst(Manifest));
        var clock = System.Diagnostics.Stopwatch.StartNew();

        var answer = await Ue4ssManifest.FetchAsync(http,
            ["https://slow.test/manifest.json", "https://fast.test/manifest.json"], "mortal-shell-2", TimeSpan.FromMilliseconds(200));

        Assert.NotNull(answer?.Release);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), $"took {clock.Elapsed}");
    }

    [Fact]
    public void The_built_in_profile_reads_the_manifest_from_cloudflare_first_and_github_last()
    {
        var urls = Assert.Single(UEManager.Core.Games.GameProfile.BuiltIn()).Ue4ssManifestUrls;

        Assert.Equal(3, urls.Count);
        Assert.StartsWith("https://metadata.openmods.nexus/", urls[0]);
        Assert.StartsWith("https://raw.githubusercontent.com/", urls[^1]);
        Assert.All(urls, url => Assert.EndsWith("/uemodmanager/manifest.json", url));
    }

    [Fact]
    public void An_update_is_offered_for_another_or_an_unknown_version()
    {
        var latest = new Ue4ssRelease("v3.0.2", "https://example.test/ue4ss.zip", null);

        Assert.True(Ue4ssManifest.IsUpdate("v3.0.1", latest));
        Assert.True(Ue4ssManifest.IsUpdate(null, latest));
        Assert.False(Ue4ssManifest.IsUpdate("V3.0.2", latest));
        Assert.False(Ue4ssManifest.IsUpdate("v3.0.1", null));
    }

    [Theory]
    [InlineData(@"C:\Downloads\mortalshell2_ue4ss_v3.0.1-1181-g83330635.zip", "v3.0.1-1181-g83330635")]
    [InlineData(@"C:\Downloads\UE4SS_v3.0.1.zip", "v3.0.1")]
    [InlineData(@"C:\Downloads\ue4ss.zip", null)]
    public void The_version_comes_from_the_file_name(string path, string? version)
    {
        Assert.Equal(version, Ue4ssManifest.VersionFromFileName(path));
    }

    private string Release(string name)
    {
        folder.File($@"{name}\dwmapi.dll");
        folder.File($@"{name}\ue4ss\UE4SS.dll");
        folder.File($@"{name}\ue4ss\UE4SS-settings.ini");
        folder.File($@"{name}\ue4ss\Mods\BPModLoaderMod\Scripts\main.lua");
        return folder.Combine(name);
    }

    [Fact]
    public void The_installed_version_is_recorded()
    {
        manager.InstallUe4ss(Release("pbz_ue4ss_v3.0.1-7-gabc"));
        Assert.Equal("v3.0.1-7-gabc", manager.InstalledUe4ssVersion);

        manager.InstallUe4ss(Release("any"), "v9.9.9");
        Assert.Equal("v9.9.9", manager.InstalledUe4ssVersion);
    }

    [Fact]
    public void Removing_clears_ue4ss_and_disables_its_mods_but_keeps_the_library()
    {
        manager.InstallUe4ss(Release("UE4SS_v3.0.1"));
        folder.File(@"Downloads\DashTest\ue4ss\Mods\DashTest\Scripts\main.lua");
        ModRecord mod;
        using (var preview = manager.Inspect(folder.Combine("Downloads", "DashTest")))
            mod = manager.Install(preview, preview.Packages.ToList(), "DashTest", null);
        Directory.CreateDirectory(Path.Combine(Win64, "ue4ss", "Mods", "ByHand"));
        File.WriteAllText(Path.Combine(Win64, "ue4ss", "Mods", "ByHand", "enabled.txt"), "");

        var result = manager.RemoveUe4ss();

        Assert.Equal(1, result.DisabledMods);
        Assert.False(Directory.Exists(Path.Combine(Win64, "ue4ss")));
        Assert.False(File.Exists(Path.Combine(Win64, "dwmapi.dll")));
        Assert.Empty(Directory.GetDirectories(Win64, "*uemm-removing*"));
        Assert.False(Ue4ssRuntime.Detect(game).Installed);
        Assert.Null(manager.InstalledUe4ssVersion);

        var disabled = Assert.Single(manager.Mods);
        Assert.False(disabled.Enabled);
        Assert.Equal(ModIntegrity.NotDeployed, ModManager.Integrity(disabled));
        Assert.True(File.Exists(Path.Combine(manager.LibraryFolderOf(mod), "Ue4ssMods", "DashTest", "Scripts", "main.lua")));

        manager.InstallUe4ss(Release("UE4SS_v3.0.1_again"));
        manager.SetEnabled(mod.Id, true);
        Assert.True(File.Exists(Path.Combine(Win64, "ue4ss", "Mods", "DashTest", "Scripts", "main.lua")));
    }

    [Fact]
    public void Removing_clears_the_older_ue4ss_2_layout()
    {
        File.WriteAllText(Path.Combine(Win64, "UE4SS.dll"), "");
        File.WriteAllText(Path.Combine(Win64, "UE4SS-settings.ini"), "");
        File.WriteAllText(Path.Combine(Win64, "xinput1_3.dll"), "");
        Directory.CreateDirectory(Path.Combine(Win64, "Mods", "OldMod"));

        manager.RemoveUe4ss();

        Assert.Equal(["TestGame-Win64-Shipping.exe"], Directory.GetFileSystemEntries(Win64).Select(Path.GetFileName));
    }

    [Fact]
    public void Without_ue4ss_nothing_is_removed()
    {
        File.WriteAllText(Path.Combine(Win64, "dwmapi.dll"), "another tool");

        Assert.Throws<ModException>(() => manager.RemoveUe4ss());

        Assert.Equal("another tool", File.ReadAllText(Path.Combine(Win64, "dwmapi.dll")));
    }

    [Fact]
    public void A_file_in_use_stops_the_removal_and_changes_nothing()
    {
        manager.InstallUe4ss(Release("UE4SS_v3.0.1"));
        var dll = Path.Combine(Win64, "dwmapi.dll");

        using (new FileStream(dll, FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.Throws<ModException>(() => manager.RemoveUe4ss());

        Assert.True(File.Exists(dll));
        Assert.True(File.Exists(Path.Combine(Win64, "ue4ss", "UE4SS.dll")));
        Assert.False(Directory.Exists(Path.Combine(Win64, "ue4ss.uemm-removing")));
    }

    /// <summary>A mod with a pak in ~mods and a Lua part in UE4SS's Mods folder.</summary>
    private ModRecord InstallMixed()
    {
        folder.File(@"Downloads\Mix\Mix_P.pak", "pak");
        folder.File(@"Downloads\Mix\ue4ss\Mods\Mix\Scripts\main.lua");
        using var preview = manager.Inspect(folder.Combine("Downloads", "Mix"));
        return manager.Install(preview, preview.Packages.ToList(), "Mix", null);
    }

    [Fact]
    public void A_locked_pak_of_a_mixed_mod_stops_the_removal_and_changes_nothing()
    {
        manager.InstallUe4ss(Release("UE4SS_v3.0.1"));
        InstallMixed();
        var pak = Path.Combine(game.ModsPath, "Mix_P.pak");

        using (File.Open(pak, FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.Throws<ModException>(() => manager.RemoveUe4ss());

        Assert.True(File.Exists(Path.Combine(Win64, "ue4ss", "UE4SS.dll")));
        Assert.True(File.Exists(Path.Combine(Win64, "dwmapi.dll")));
        Assert.True(File.Exists(Path.Combine(Win64, "ue4ss", "Mods", "Mix", "Scripts", "main.lua")));
        Assert.True(File.Exists(pak));
        Assert.Equal(ModIntegrity.Intact, ModManager.Integrity(Assert.Single(manager.Mods)));

        manager.RemoveUe4ss();
        Assert.False(File.Exists(pak));
        Assert.False(Assert.Single(manager.Mods).Enabled);
    }

    [Fact]
    public void A_removal_that_cannot_be_saved_changes_nothing()
    {
        manager.InstallUe4ss(Release("UE4SS_v3.0.1"));
        InstallMixed();
        var list = new AppPaths(folder.Combine("Data")).StateFile(game.Profile.Id);

        using (File.Open(list, FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.Throws<ModException>(() => manager.RemoveUe4ss());

        Assert.True(File.Exists(Path.Combine(Win64, "ue4ss", "UE4SS.dll")));
        Assert.True(File.Exists(Path.Combine(game.ModsPath, "Mix_P.pak")));
        Assert.Equal(ModIntegrity.Intact, ModManager.Integrity(Assert.Single(manager.Mods)));
        Assert.Equal("v3.0.1", manager.InstalledUe4ssVersion);
    }

    private sealed class FixedResponse(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8) });
    }

    /// <summary>An answer per address; every address asked is noted.</summary>
    private sealed class ByAddress(List<string> asked, Dictionary<string, (HttpStatusCode Status, string Body)> answers) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            asked.Add(url);
            var (status, body) = answers[url];
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8) });
        }
    }

    /// <summary>The slow address never answers; any other one answers at once.</summary>
    private sealed class SlowFirst(string body) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.Host == "slow.test")
                await Task.Delay(Timeout.Infinite, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8) };
        }
    }
}
