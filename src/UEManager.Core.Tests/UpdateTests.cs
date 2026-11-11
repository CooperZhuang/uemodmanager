using System.Diagnostics;
using System.Net;
using System.Text;
using UEManager.Core.Mods;
using UEManager.Core.Updates;

namespace UEManager.Core.Tests;

public class UpdateTests
{
    private const string Release = """
        {
          "tag_name": "v1.1.0",
          "html_url": "https://github.com/KintaroModding/uemodmanager/releases/tag/v1.1.0",
          "published_at": "2026-11-02T18:00:00Z",
          "body": "Faster installs.",
          "assets": [
            { "name": "uemm-full_phantom-blade-zero_1.1.0.zip", "size": 200,
              "browser_download_url": "https://github.com/KintaroModding/uemodmanager/releases/download/v1.1.0/uemm-full_phantom-blade-zero_1.1.0.zip",
              "digest": "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" },
            { "name": "uemm_mortal-shell-2_1.1.0.zip", "size": 100,
              "browser_download_url": "https://github.com/KintaroModding/uemodmanager/releases/download/v1.1.0/uemm_mortal-shell-2_1.1.0.zip" },
            { "name": "uemm_phantom-blade-zero_1.1.0.zip", "size": 150,
              "browser_download_url": "https://github.com/KintaroModding/uemodmanager/releases/download/v1.1.0/uemm_phantom-blade-zero_1.1.0.zip",
              "digest": "sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb" }
          ]
        }
        """;

    [Fact]
    public void Picks_the_zip_of_this_build_and_game()
    {
        var release = AppReleases.Parse(Release, "uemm_phantom-blade-zero_");

        Assert.Equal("1.1.0", release.Version);
        Assert.Equal("Faster installs.", release.Notes);
        Assert.Equal("https://github.com/KintaroModding/uemodmanager/releases/tag/v1.1.0", release.PageUrl);
        Assert.Equal("uemm_phantom-blade-zero_1.1.0.zip", release.File?.Name);
        Assert.Equal(150, release.File?.Size);
        Assert.Equal(new string('b', 64), release.File?.Sha256);
    }

    /// <summary>
    /// Cloudflare's copy of a release since 2.0.0: one zip for every game, listed twice: by its own name, and by the
    /// name apps up to 1.7.2 look for, with the same link, size and digest. The alias comes first here on purpose.
    /// </summary>
    private const string Neutral = """
        {
          "tag_name": "v2.0.0",
          "html_url": "https://github.com/KintaroModding/uemodmanager/releases/tag/v2.0.0",
          "assets": [
            { "name": "uemm_phantom-blade-zero_2.0.0.zip", "size": 300,
              "browser_download_url": "uemm_2.0.0.zip",
              "github_download_url": "https://github.com/KintaroModding/uemodmanager/releases/download/v2.0.0/uemm_phantom-blade-zero_2.0.0.zip",
              "digest": "sha256:dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd" },
            { "name": "uemm_2.0.0.zip", "size": 300,
              "browser_download_url": "uemm_2.0.0.zip",
              "github_download_url": "https://github.com/KintaroModding/uemodmanager/releases/download/v2.0.0/uemm_2.0.0.zip",
              "digest": "sha256:dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd" }
          ]
        }
        """;

    private static readonly Uri CopyList = new("https://metadata.openmods.nexus/uemodmanager/app/latest.json");

    [Fact]
    public void The_app_takes_the_zip_for_every_game_by_its_whole_name()
    {
        var release = AppReleases.Parse(Neutral, "", listUrl: CopyList, fileName: v => $"uemm_{v}.zip");

        Assert.Equal("2.0.0", release.Version);
        Assert.Equal("uemm_2.0.0.zip", release.File?.Name);
        Assert.Equal(["https://metadata.openmods.nexus/uemodmanager/app/uemm_2.0.0.zip",
            "https://github.com/KintaroModding/uemodmanager/releases/download/v2.0.0/uemm_2.0.0.zip"], release.File?.Urls);
        // A release with only the old names has no zip for this app; one for another version neither.
        Assert.Null(AppReleases.Parse(Release, "", fileName: v => $"uemm_{v}.zip").File);
        Assert.Null(AppReleases.Parse(Neutral, "", fileName: v => $"uemm_{v}.1.zip").File);
    }

    [Fact]
    public void An_app_up_to_1_7_2_finds_the_same_zip_under_its_old_name()
    {
        // As those apps ask: by the start of the name, Phantom Blade Zero's.
        var release = AppReleases.Parse(Neutral, "uemm_phantom-blade-zero_", listUrl: CopyList);

        Assert.Equal("uemm_phantom-blade-zero_2.0.0.zip", release.File?.Name);
        Assert.Equal("https://metadata.openmods.nexus/uemodmanager/app/uemm_2.0.0.zip", release.File?.Url);
        Assert.Equal(new string('d', 64), release.File?.Sha256);
    }

    /// <summary>A copy of GitHub's answer next to a copy of the zip, as on Cloudflare.</summary>
    private const string Copy = """
        {
          "tag_name": "v1.5.2",
          "html_url": "https://github.com/KintaroModding/uemodmanager/releases/tag/v1.5.2",
          "assets": [
            { "name": "uemm_phantom-blade-zero_1.5.2.zip", "size": 150,
              "browser_download_url": "uemm_phantom-blade-zero_1.5.2.zip",
              "github_download_url": "https://github.com/KintaroModding/uemodmanager/releases/download/v1.5.2/uemm_phantom-blade-zero_1.5.2.zip",
              "digest": "sha256:cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc" }
          ]
        }
        """;

    [Fact]
    public void A_copy_links_the_zip_next_to_it_and_the_same_zip_on_GitHub()
    {
        var release = AppReleases.Parse(Copy, "uemm_phantom-blade-zero_",
            listUrl: new Uri("https://metadata.openmods.nexus/uemodmanager/app/latest.json"));

        Assert.Equal("1.5.2", release.Version);
        Assert.Equal(
            ["https://metadata.openmods.nexus/uemodmanager/app/uemm_phantom-blade-zero_1.5.2.zip",
             "https://github.com/KintaroModding/uemodmanager/releases/download/v1.5.2/uemm_phantom-blade-zero_1.5.2.zip"],
            release.File?.Urls);
        Assert.Equal(new string('c', 64), release.File?.Sha256);
        // GitHub's own answer has one link only.
        Assert.Single(AppReleases.Parse(Release, "uemm_phantom-blade-zero_").File!.Urls);
    }

    [Fact]
    public async Task The_release_list_is_read_from_the_first_address_that_answers()
    {
        var asked = new List<string>();
        using var http = new HttpClient(new Answers(asked, new()
        {
            ["https://first.test/app/latest.json"] = (HttpStatusCode.NotFound, ""),
            ["https://second.test/app/latest.json"] = (HttpStatusCode.OK, Copy),
        }));

        var release = await AppReleases.FetchLatestAsync(http,
            ["https://first.test/app/latest.json", "https://second.test/app/latest.json", "https://third.test/app/latest.json"],
            "uemm_phantom-blade-zero_");

        Assert.Equal("https://second.test/app/uemm_phantom-blade-zero_1.5.2.zip", release.File?.Url);
        Assert.Equal("https://second.test/app/latest.json", release.ListUrl);
        Assert.Equal(["https://first.test/app/latest.json", "https://second.test/app/latest.json"], asked);
    }

    [Fact]
    public async Task When_no_address_answers_the_last_ones_problem_is_reported()
    {
        using var http = new HttpClient(new Answers([], new()
        {
            ["https://first.test/latest.json"] = (HttpStatusCode.NotFound, ""),
            ["https://second.test/latest.json"] = (HttpStatusCode.Forbidden, ""),
        }));

        var error = await Assert.ThrowsAsync<ModException>(() => AppReleases.FetchLatestAsync(http,
            ["https://first.test/latest.json", "https://second.test/latest.json"], "uemm_"));

        Assert.Contains("403", error.Message);
    }

    [Fact]
    public async Task A_download_that_fails_on_the_copy_comes_from_GitHub_and_is_checked()
    {
        using var folder = new TestFolder();
        var zip = Encoding.UTF8.GetBytes("the new app");
        var sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(zip));
        using var http = new HttpClient(new Bytes(new()
        {
            ["https://copy.test/app.zip"] = (HttpStatusCode.NotFound, []),
            ["https://github.test/app.zip"] = (HttpStatusCode.OK, zip),
        }));

        var path = await FileDownloader.DownloadAsync(http, ["https://copy.test/app.zip", "https://github.test/app.zip"],
            "app.zip", zip.Length, sha256, folder.Combine("downloads"), "UE MM");

        Assert.Equal(zip, File.ReadAllBytes(path));
    }

    [Fact]
    public async Task A_changed_copy_is_refused_and_the_next_address_is_tried()
    {
        using var folder = new TestFolder();
        var zip = Encoding.UTF8.GetBytes("the new app");
        var sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(zip));
        using var http = new HttpClient(new Bytes(new()
        {
            ["https://copy.test/app.zip"] = (HttpStatusCode.OK, Encoding.UTF8.GetBytes("not the app")),
            ["https://github.test/app.zip"] = (HttpStatusCode.OK, zip),
        }));

        var path = await FileDownloader.DownloadAsync(http, ["https://copy.test/app.zip", "https://github.test/app.zip"],
            "app.zip", null, sha256, folder.Combine("downloads"), "UE MM");

        Assert.Equal(zip, File.ReadAllBytes(path));
    }

    /// <summary>A text answer per address; every address asked is noted.</summary>
    private sealed class Answers(List<string> asked, Dictionary<string, (HttpStatusCode Status, string Body)> answers) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            asked.Add(url);
            var (status, body) = answers[url];
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8) });
        }
    }

    /// <summary>A file per address.</summary>
    private sealed class Bytes(Dictionary<string, (HttpStatusCode Status, byte[] Body)> answers) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var (status, body) = answers[request.RequestUri!.ToString()];
            return Task.FromResult(new HttpResponseMessage(status) { Content = new ByteArrayContent(body) });
        }
    }

    [Fact]
    public void A_build_without_a_zip_gets_none()
    {
        var release = AppReleases.Parse(Release, "uemm_wukong_");

        Assert.Equal("1.1.0", release.Version);
        Assert.Null(release.File);
    }

    [Fact]
    public void Http_links_count_only_for_a_test_list()
    {
        var json = Release.Replace("https://github.com/KintaroModding/uemodmanager/releases/download", "http://localhost:8765");

        Assert.Null(AppReleases.Parse(json, "uemm_phantom-blade-zero_").File);
        Assert.Equal("http://localhost:8765/v1.1.0/uemm_phantom-blade-zero_1.1.0.zip",
            AppReleases.Parse(json, "uemm_phantom-blade-zero_", allowHttp: true).File?.Url);
    }

    [Fact]
    public void A_release_without_a_version_is_refused()
    {
        Assert.Throws<ModException>(() => AppReleases.Parse("""{ "assets": [] }""", "uemm_"));
    }

    [Fact]
    public void Replaces_the_exe_and_cleans_up_on_the_next_start()
    {
        using var folder = new TestFolder();
        var current = folder.File("App.exe", "old version");
        var update = folder.File(@"update\App.exe", "new version");

        Assert.False(SelfUpdate.IsJustUpdated(current));
        SelfUpdate.Replace(current, update);

        Assert.Equal("new version", File.ReadAllText(current));
        Assert.Equal("old version", File.ReadAllText(current + ".old"));
        Assert.True(SelfUpdate.IsJustUpdated(current));
        SelfUpdate.CleanUp(current);
        Assert.Equal(["App.exe"], Directory.GetFiles(folder.Path).Select(Path.GetFileName));
        Assert.False(SelfUpdate.IsJustUpdated(current));
    }

    [Fact]
    public void Only_an_old_version_an_update_left_counts_as_just_updated()
    {
        using var folder = new TestFolder();
        var current = folder.File("App.exe", "app");
        folder.File("App.exe.personal-backup.old", "the player's own copy");
        folder.File("App.exe.new", "not used");
        Assert.False(SelfUpdate.IsJustUpdated(current));

        folder.File($"App.exe.{new string('b', 32)}.old", "old, name taken");
        Assert.True(SelfUpdate.IsJustUpdated(current));
    }

    [Fact]
    public void The_clean_up_deletes_only_what_an_update_left()
    {
        using var folder = new TestFolder();
        var current = folder.File("App.exe", "app");
        folder.File("App.exe.old", "old");
        folder.File($"App.exe.{new string('a', 32)}.old", "old, name taken");
        folder.File("App.exe.new", "not used");
        folder.File("App.exe.personal-backup.old", "the player's own copy");
        folder.File("App.exe.12345.old", "the player's own copy");

        SelfUpdate.CleanUp(current);

        Assert.Equal(["App.exe", "App.exe.12345.old", "App.exe.personal-backup.old"],
            Directory.GetFiles(folder.Path).Select(Path.GetFileName).Order());
    }

    [Fact]
    public void An_exe_that_cannot_be_moved_stays_as_it_was()
    {
        using var folder = new TestFolder();
        var current = folder.File("App.exe", "old version");
        var update = folder.File(@"update\App.exe", "new version");

        // Unlike a running exe, a file held this way cannot even be renamed.
        using (File.Open(current, FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.Throws<ModException>(() => SelfUpdate.Replace(current, update));

        Assert.Equal("old version", File.ReadAllText(current));
        Assert.Equal(["App.exe"], Directory.GetFiles(folder.Path).Select(Path.GetFileName));
    }

    [Fact]
    public void The_new_exe_must_be_the_announced_version()
    {
        using var folder = new TestFolder();
        // Any program file with version information will do: the test assembly carries the app's version.
        var assembly = typeof(UpdateTests).Assembly.Location;
        var version = FileVersionInfo.GetVersionInfo(assembly).ProductVersion!.Split('+')[0];
        File.Copy(assembly, folder.Combine("UEModManager.exe"));

        Assert.Equal(folder.Combine("UEModManager.exe"), SelfUpdate.FindExecutable(folder.Path, version));
        Assert.Throws<ModException>(() => SelfUpdate.FindExecutable(folder.Path, "99.0.0"));
    }
}
