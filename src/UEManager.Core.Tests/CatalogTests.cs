using System.Net;
using System.Text;
using UEManager.Core.Catalog;
using UEManager.Core.Mods;

namespace UEManager.Core.Tests;

public class CatalogTests
{
    private const string Games = """
        {
          "openmods": 1,
          "futureField": { "ignored": true },
          "games": [
            { "slug": "minecraft", "name": "Minecraft", "steamAppId": null, "mods": 1,
              "list": "https://openmods.nexus/games/minecraft/mods.json" },
            { "slug": "mortal-shell-ii", "name": "Mortal Shell II", "steamAppId": "2584270", "mods": 6,
              "list": "https://openmods.nexus/games/mortal-shell-ii/mods.json" },
            { "slug": "broken", "name": "No list" }
          ]
        }
        """;

    private const string List = """
        {
          "openmods": 1,
          "game": { "slug": "mortal-shell-ii", "name": "Mortal Shell II", "steamAppId": "2584270",
                    "art": "https://cdn.example.test/header.jpg", "page": "https://openmods.nexus/games/mortal-shell-ii" },
          "mods": [
            { "name": "Dash Enhanced", "summary": "Longer dashes.", "author": "KintaroModding", "listedBy": null,
              "cover": "https://raw.githubusercontent.com/KintaroModding/mortalshell2_dashenhanced/main/docs/cover.png",
              "tags": ["gameplay"], "license": "MIT",
              "page": "https://openmods.nexus/mods/kintaromodding/mortalshell2_dashenhanced",
              "source": "https://github.com/KintaroModding/mortalshell2_dashenhanced",
              "updated": "2026-10-23T14:11:35Z", "newField": 1,
              "release": { "tag": "v1.0", "date": "2026-10-23T14:11:35Z",
                "latest": "https://github.com/KintaroModding/mortalshell2_dashenhanced/releases/latest",
                "files": [ { "name": "mortalshell2_dashenhanced_1.0.zip", "size": 17239,
                  "url": "https://github.com/KintaroModding/mortalshell2_dashenhanced/releases/download/v1.0/mortalshell2_dashenhanced_1.0.zip",
                  "sha256": "f20f89b8c6f6f2aa737fb54e76e68c818cfacd490fd9c5c712d4c438d09b1ab0" } ] } },
            { "name": "No release", "author": "KintaroModding", "cover": null, "tags": [],
              "page": "file:///C:/Windows/notepad.exe", "source": "https://github.com/KintaroModding/norelease", "release": null },
            { "name": "Empty release", "source": "https://github.com/KintaroModding/empty",
              "release": { "tag": "v1.0", "files": [] } },
            { "name": "UE4SS", "tags": ["tools", "library"], "source": "https://github.com/KintaroModding/ue4ss",
              "release": { "tag": "v3.0.1", "files": [
                { "name": "mortalshell2_ue4ss_v3.0.1.zip", "size": 10, "url": "https://example.test/a.zip" },
                { "name": "expedition33_ue4ss_v3.0.1.zip", "size": 10, "url": "https://example.test/b.zip" },
                { "name": "SHA256SUMS.txt", "size": 10, "url": "https://example.test/sums.txt" } ] } },
            { "name": "Repack", "author": "someone", "listedBy": "KintaroModding", "source": "https://github.com/Someone/Repack.git",
              "release": { "tag": "1.2", "files": [
                { "name": "Repack v1.2.zip", "size": 10, "url": "https://example.test/Repack v1.2.zip", "sha256": "not-a-checksum" },
                { "name": "readme.txt", "size": 1, "url": "https://example.test/readme.txt" } ] } },
            { "summary": "No name, so it is left out", "source": "https://github.com/x/y" }
          ]
        }
        """;

    [Fact]
    public void Finds_the_game_by_its_steam_app_id()
    {
        var games = OpenMods.ParseGames(Games);

        Assert.Equal(["minecraft", "mortal-shell-ii"], games.Select(g => g.Slug));
        var game = OpenMods.FindGame(games, "2584270");
        Assert.Equal("https://openmods.nexus/games/mortal-shell-ii/mods.json", game?.ListUrl);
        Assert.Equal(6, game?.ModCount);
        Assert.Null(OpenMods.FindGame(games, "4115450"));
        Assert.Null(OpenMods.FindGame(games, null));
    }

    [Theory]
    [InlineData("""{ "openmods": 2, "games": [] }""", "newer")]
    [InlineData("""{ "games": [] }""", "not an openmods list")]
    [InlineData("""[ 1, 2 ]""", "not an openmods list")]
    public void Refuses_other_formats(string json, string message)
    {
        var error = Assert.Throws<ModException>(() => OpenMods.ParseGames(json));

        Assert.Contains(message, error.Message);
    }

    [Fact]
    public void Reads_the_mods_of_a_game()
    {
        var list = OpenMods.ParseList(List);

        Assert.Equal("https://cdn.example.test/header.jpg", list.Game.Art);
        Assert.Equal(["Dash Enhanced", "No release", "Empty release", "UE4SS", "Repack"], list.Mods.Select(m => m.Name));

        var dash = list.Mods[0];
        Assert.Equal("kintaromodding/mortalshell2_dashenhanced", dash.Id);
        Assert.Equal("1.0", dash.Release?.Version);
        Assert.Equal(["gameplay"], dash.Tags);
        Assert.Equal(17239, dash.InstallFile?.Size);
        Assert.Equal("f20f89b8c6f6f2aa737fb54e76e68c818cfacd490fd9c5c712d4c438d09b1ab0", dash.InstallFile?.Sha256);
        Assert.False(dash.IsUe4ss);
    }

    [Fact]
    public void Mods_without_a_single_download_get_no_install_file()
    {
        var mods = OpenMods.ParseList(List).Mods;

        Assert.Null(mods[1].Release);
        Assert.Null(mods[1].InstallFile);
        Assert.Null(mods[2].InstallFile);
        Assert.True(mods[3].IsUe4ss);
        Assert.Null(mods[3].InstallFile);
    }

    [Fact]
    public void Only_https_links_are_kept()
    {
        var noRelease = OpenMods.ParseList(List).Mods[1];

        Assert.Null(noRelease.Page);
        Assert.Null(noRelease.Cover);
    }

    private const string Screenshots = """
        {
          "openmods": 1,
          "mods": [
            { "name": "Map", "source": "https://github.com/A/map", "cover": "https://raw.githubusercontent.com/A/map/main/docs/cover.png",
              "screenshots": [
                { "url": "https://raw.githubusercontent.com/A/map/main/docs/shot-1.png", "alt": "  The map  " },
                { "url": "https://i.imgur.com/abcd123.png" },
                { "url": "https://raw.githubusercontent.com/A/map/main/docs/cover.png", "alt": "The cover again" },
                { "url": "https://i.imgur.com/abcd123.png", "alt": "Twice" },
                { "url": "http://example.test/plain.png" },
                { "alt": "No address" },
                "https://example.test/plain-address.png",
                42
              ] },
            { "name": "No cover", "source": "https://github.com/A/nocover",
              "screenshots": [ { "url": "https://example.test/only.png" } ] },
            { "name": "Cover only", "source": "https://github.com/A/coveronly", "cover": "https://example.test/cover.png" },
            { "name": "Nothing", "source": "https://github.com/A/nothing" },
            { "name": "Many", "source": "https://github.com/A/many", "screenshots": [
              { "url": "https://e.test/1.png" }, { "url": "https://e.test/2.png" }, { "url": "https://e.test/3.png" },
              { "url": "https://e.test/4.png" }, { "url": "https://e.test/5.png" }, { "url": "https://e.test/6.png" },
              { "url": "https://e.test/7.png" }, { "url": "https://e.test/8.png" }, { "url": "https://e.test/9.png" },
              { "url": "https://e.test/10.png" }, { "url": "https://e.test/11.png" }, { "url": "https://e.test/12.png" },
              { "url": "https://e.test/13.png" }, { "url": "https://e.test/14.png" }, { "url": "https://e.test/15.png" },
              { "url": "https://e.test/16.png" }, { "url": "https://e.test/17.png" }, { "url": "https://e.test/18.png" },
              { "url": "https://e.test/19.png" }, { "url": "https://e.test/20.png" }, { "url": "https://e.test/21.png" } ] }
          ]
        }
        """;

    [Fact]
    public void Reads_the_screenshots_with_the_cover_first_in_the_viewer()
    {
        var mods = OpenMods.ParseList(Screenshots).Mods;

        // https only, in the list's order; the cover and a picture that comes twice are left out; alt texts trimmed.
        var map = mods[0];
        Assert.Equal(["https://raw.githubusercontent.com/A/map/main/docs/shot-1.png", "https://i.imgur.com/abcd123.png",
            "https://example.test/plain-address.png"], map.Screenshots.Select(p => p.Url));
        Assert.Equal(["The map", null, null], map.Screenshots.Select(p => p.Alt));
        Assert.Equal("https://raw.githubusercontent.com/A/map/main/docs/cover.png", map.Pictures[0].Url);
        Assert.Equal(4, map.Pictures.Count);

        // Without a cover the viewer starts with the first screenshot; with neither it has nothing to show.
        Assert.Equal(["https://example.test/only.png"], mods[1].Pictures.Select(p => p.Url));
        Assert.Equal(["https://example.test/cover.png"], mods[2].Pictures.Select(p => p.Url));
        Assert.Empty(mods[2].Screenshots);
        Assert.Empty(mods[3].Pictures);

        // At most 20, the first 20, as openmods lists them.
        Assert.Equal(20, mods[4].Screenshots.Count);
        Assert.Equal("https://e.test/20.png", mods[4].Screenshots[^1].Url);
    }

    [Fact]
    public void Another_authors_project_keeps_its_own_id_and_file()
    {
        var repack = OpenMods.ParseList(List).Mods[4];

        Assert.Equal("someone/repack", repack.Id);
        Assert.Equal("KintaroModding", repack.ListedBy);
        Assert.Equal("1.2", repack.Release?.Version);
        Assert.Equal("Repack v1.2.zip", repack.InstallFile?.Name);
        Assert.Equal("https://example.test/Repack%20v1.2.zip", repack.InstallFile?.Url);
        Assert.Null(repack.InstallFile?.Sha256);
    }

    /// <summary>A list as openmods gives it since 2026-11-13: the paths in each release zip it has read.</summary>
    private const string Entries = """
        {
          "openmods": 1,
          "mods": [
            { "name": "Live by the Blade", "tags": ["gameplay"], "source": "https://github.com/KintaroModding/phantombladezero_livebytheblade",
              "release": { "tag": "v1.5", "files": [ { "name": "phantombladezero_livebytheblade_1.5.zip", "size": 8621, "url": "https://example.test/lbtb.zip",
                "entries": [ "ue4ss/Mods/phantombladezero_livebytheblade/enabled.txt", 5,
                             "ue4ss\\Mods\\phantombladezero_livebytheblade\\Scripts\\config.lua",
                             "ue4ss/Mods/phantombladezero_livebytheblade/Scripts/main.lua" ] } ] } },
            { "name": "Not read yet", "tags": ["gameplay"], "source": "https://github.com/KintaroModding/new",
              "release": { "tag": "v1.0", "files": [ { "name": "new_1.0.zip", "size": 10, "url": "https://example.test/new.zip" } ] } },
            { "name": "Cut", "tags": ["gameplay"], "source": "https://github.com/KintaroModding/cut",
              "release": { "tag": "v1.0", "files": [ { "name": "cut_1.0.zip", "size": 10, "url": "https://example.test/cut.zip",
                "entries": [ "cut_P.pak" ], "entriesTruncated": true } ] } },
            { "name": "UE MM", "tags": ["Tools"], "source": "https://github.com/KintaroModding/uemodmanager",
              "release": { "tag": "v2.1.0", "files": [
                { "name": "uemm_2.1.0.zip", "size": 10, "url": "https://example.test/a.zip", "entries": [ "UEModManager.exe" ] },
                { "name": "uemm_phantom-blade-zero_2.1.0.zip", "size": 10, "url": "https://example.test/b.zip", "entries": [ "UEModManager.exe" ] } ] } },
            { "name": "USMAP File", "tags": ["tools"], "source": "https://github.com/KintaroModding/phantombladezero_pbzusmap",
              "release": { "tag": "v1.1", "files": [
                { "name": "phantombladezero_pbzusmap_1.1.zip", "size": 10, "url": "https://example.test/u.zip", "entries": [ "ProjectPBZ-5.7.4-302256-full.usmap" ] },
                { "name": "ProjectPBZ-5.7.4-302256-full.usmap", "size": 10, "url": "https://example.test/u.usmap" } ] } },
            { "name": "UE4SS", "tags": ["tools", "library"], "source": "https://github.com/KintaroModding/ue4ss",
              "release": { "tag": "v4.0.0", "files": [ { "name": "phantombladezero_ue4ss_v4.0.0.zip", "size": 10, "url": "https://example.test/ue4ss.zip",
                "entries": [ "dwmapi.dll", "ue4ss/UE4SS.dll", "ue4ss/Mods/Keybinds/Scripts/main.lua" ] } ] } },
            { "name": "A library", "tags": ["library"], "source": "https://github.com/Someone/library",
              "release": null }
          ]
        }
        """;

    [Fact]
    public void Reads_the_paths_in_each_zip()
    {
        var mods = OpenMods.ParseList(Entries).Mods;

        var file = mods[0].InstallFile!;
        Assert.Equal(["ue4ss/Mods/phantombladezero_livebytheblade/enabled.txt",
            "ue4ss\\Mods\\phantombladezero_livebytheblade\\Scripts\\config.lua",
            "ue4ss/Mods/phantombladezero_livebytheblade/Scripts/main.lua"], file.Entries);
        Assert.False(file.EntriesTruncated);
        Assert.Null(mods[1].InstallFile?.Entries);
        Assert.True(mods[2].InstallFile?.EntriesTruncated);
        Assert.Null(mods[4].Release?.Files[1].Entries);
    }

    [Fact]
    public void A_list_of_more_paths_than_openmods_gives_is_cut()
    {
        var paths = string.Join(", ", Enumerable.Range(0, 2001).Select(i => $"\"file{i}.pak\""));
        var json = $$"""
            { "openmods": 1, "mods": [ { "name": "Big", "source": "https://github.com/x/big",
              "release": { "tag": "v1", "files": [ { "name": "big.zip", "size": 1, "url": "https://example.test/big.zip", "entries": [ {{paths}} ] } ] } } ] }
            """;

        var file = OpenMods.ParseList(json).Mods[0].InstallFile!;

        Assert.Equal(2000, file.Entries?.Count);
        Assert.True(file.EntriesTruncated);
        Assert.Null(OpenMods.ParseList(json).Mods[0].TypeLabelIn(null, null));
    }

    [Fact]
    public void The_type_is_known_for_a_single_zip_that_openmods_has_read_in_full()
    {
        var mods = OpenMods.ParseList(Entries).Mods;

        Assert.Equal("UE4SS Lua", mods[0].TypeLabelIn("ProjectPBZ", "platform/steam"));
        Assert.Null(mods[1].TypeLabelIn("ProjectPBZ", "platform/steam")); // not read yet
        Assert.Null(mods[2].TypeLabelIn("ProjectPBZ", "platform/steam")); // the list of paths was cut
        Assert.Null(mods[3].TypeLabelIn("ProjectPBZ", "platform/steam")); // two zips, so no single install file
        Assert.Equal(ModTypes.Other, mods[4].TypeLabelIn("ProjectPBZ", "platform/steam")); // the zip beside the loose .usmap
        Assert.Null(mods[5].TypeLabelIn("ProjectPBZ", "platform/steam")); // UE4SS itself is no mod
    }

    [Fact]
    public void Tools_are_the_mods_tagged_so_in_any_case()
    {
        var mods = OpenMods.ParseList(Entries).Mods;

        Assert.Equal(["UE MM", "USMAP File", "UE4SS"], mods.Where(m => m.IsTool).Select(m => m.Name));
        Assert.False(mods[6].IsTool);
    }

    [Theory]
    // The zip layouts of KintaroModding's releases, as openmods lists them.
    [InlineData("UE4SS Lua", "ue4ss/Mods/witchtime/enabled.txt|ue4ss/Mods/witchtime/Scripts/config.lua|ue4ss/Mods/witchtime/Scripts/main.lua")]
    [InlineData("UE4SS DLL", "CONTRIBUTORS.md|LICENSE|README.md|ue4ss/Mods/cppbypass/dlls/main.dll|ue4ss/Mods/cppbypass/enabled.txt|UE4SS-SDK-LICENSE.txt")]
    [InlineData("PAK", "animationcancel_P.pak|animationcancel_P.utoc|animationcancel_P.ucas|animationcancel.txt")]
    [InlineData("Other", "UEModManager.exe")]
    [InlineData("Other", "ProjectPBZ-5.7.4-302256-full.usmap")]
    // Other authors' layouts.
    [InlineData("UE4SS Lua + DLL", "Mods/Both/Scripts/main.lua|Mods/Both/dlls/main.dll")]
    [InlineData("UE4SS Lua", "MyMod/SCRIPTS/Main.LUA|MyMod/enabled.txt")]
    [InlineData("UE4SS Lua", "Scripts/main.lua|enabled.txt")]
    [InlineData("UE4SS Lua", "ue4ss/Mods/lua/Scripts/main.lua|Content/Paks/~mods/assets_P.pak")]
    [InlineData("PAK", "Content/Paks/~mods/a_P.pak|screenshot.png")]
    [InlineData("Blueprint", "LogicMods/a.pak|LogicMods/a.utoc|LogicMods/a.ucas")]
    [InlineData("UE4SS files", "ue4ss/Mods/ModConfig/settings.json")]
    [InlineData("Other", "readme.txt|ue4ss/Mods/mods.txt|ue4ss/Mods/shared/lib/lib.lua|Mods/Scripts/main.lua|ue4ss/Mods/x/Scripts/")]
    [InlineData("Other", "")]
    public void The_type_comes_from_the_paths_in_the_zip(string type, string paths)
    {
        Assert.Equal(type, CatalogTypes.Label(paths.Split('|', StringSplitOptions.RemoveEmptyEntries)));
    }

    [Theory]
    // PBZModLoader's zip: its entry DLL next to the exe and its scripts below the project folder are game files; its
    // other entries for other DLL names wait in a folder of their own, and an install leaves them out.
    [InlineData("Game files", "PBZModLoader-Install-Guide.txt|alternative-entries/dsound/dsound.dll|alternative-entries/dxgi/dxgi.dll|alternative-entries/hid/hid.dll|alternative-entries/winmm/winmm.dll|platform/steam/version.dll|platform/wegame/version.dll|ProjectPBZ/PBZModLoader/LICENSES.txt|ProjectPBZ/Content/JavaScript/PBZModLoader/bootstrap.js")]
    [InlineData("Game files", "ProjectPBZ/Saved/Config/Windows/Input.ini")]
    // A mod for PBZModLoader, as KintaroModding's Animation Cancel 2.0 zip: its type is the loader it runs in.
    [InlineData("PBZModLoader", "ProjectPBZ/PBZModLoader/mods/phantombladezero_animationcancel/main.js|ProjectPBZ/PBZModLoader/mods/phantombladezero_animationcancel/phantombladezero_animationcancel.txt")]
    [InlineData("Exe files", "dxgi.dll|ReShade.ini|reshade-shaders/Shaders/Glow.fx")]
    [InlineData("PAK", "ProjectPBZ/Content/Paks/~mods/a_P.pak|ProjectPBZ/Content/Paks/~mods/a_P.utoc|ProjectPBZ/Content/Paks/~mods/a_P.ucas")]
    [InlineData("Other", "ProjectPBZ-5.7.4-302256-full.usmap")]
    [InlineData("Other", "UEModManager.exe")]
    // Paths that would leave the download are left out, never followed.
    [InlineData("Other", "../outside_P.pak|C:/Windows/evil_P.pak|/rooted_P.pak|a/./b_P.pak")]
    public void With_the_games_folders_the_type_is_the_one_the_install_gives(string type, string paths)
    {
        Assert.Equal(type, CatalogTypes.Label(paths.Split('|', StringSplitOptions.RemoveEmptyEntries), "ProjectPBZ", "platform/steam"));
    }

    [Theory]
    [InlineData("https://github.com/KintaroModding/Repo", "kintaromodding/repo")]
    [InlineData("https://github.com/KintaroModding/repo.git", "kintaromodding/repo")]
    [InlineData("https://github.com/KintaroModding/repo/tree/main", "kintaromodding/repo")]
    [InlineData("https://gitlab.com/Someone/Repo/", "https://gitlab.com/someone/repo")]
    public void The_mod_id_comes_from_the_source(string source, string id)
    {
        Assert.Equal(id, OpenMods.IdFromSource(source));
    }

    [Theory]
    [InlineData("1.0", "1.1", true)]
    [InlineData("1.9", "1.10", true)]
    [InlineData("1.10", "1.9", false)]
    [InlineData("1.0", "v1.0", false)]
    [InlineData("1", "1.0", false)]
    [InlineData(null, "1.0", true)]
    [InlineData("beta", "beta", false)]
    [InlineData("beta", "rc", true)]
    public void Offers_only_newer_versions(string? installed, string latest, bool update)
    {
        Assert.Equal(update, ModVersion.IsUpdate(installed, latest));
    }

    [Fact]
    public async Task Fetches_the_list_of_the_game()
    {
        using var http = new HttpClient(new Pages(new()
        {
            ["https://openmods.nexus/games/mods.json"] = Games,
            ["https://openmods.nexus/games/mortal-shell-ii/mods.json"] = List,
        }));

        var list = await OpenMods.FetchAsync(http, "https://openmods.nexus/games/mods.json", "2584270");
        var none = await OpenMods.FetchAsync(http, "https://openmods.nexus/games/mods.json", "4115450");

        Assert.Equal(5, list?.Mods.Count);
        Assert.Null(none);
    }

    [Fact]
    public async Task A_list_that_cannot_be_loaded_is_reported()
    {
        using var http = new HttpClient(new Pages([]));

        var error = await Assert.ThrowsAsync<ModException>(() => OpenMods.FetchAsync(http, "https://openmods.nexus/games/mods.json", "2584270"));

        Assert.Contains("404", error.Message);
    }

    [Fact]
    public async Task A_download_with_another_size_or_checksum_is_refused_and_removed()
    {
        using var folder = new TestFolder();
        using var http = new HttpClient(new Pages(new() { ["https://example.test/mod.zip"] = "zip bytes" }));
        var sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData("zip bytes"u8.ToArray()));

        var file = await FileDownloader.DownloadAsync(http, "https://example.test/mod.zip", "mod.zip", 9, sha256, folder.Combine("a"), "Mod");
        var size = await Assert.ThrowsAsync<ModException>(() =>
            FileDownloader.DownloadAsync(http, "https://example.test/mod.zip", "mod.zip", 10, null, folder.Combine("b"), "Mod"));
        var checksum = await Assert.ThrowsAsync<ModException>(() =>
            FileDownloader.DownloadAsync(http, "https://example.test/mod.zip", "mod.zip", 9, new string('0', 64), folder.Combine("c"), "Mod"));

        Assert.Equal("zip bytes", File.ReadAllText(file));
        Assert.Contains("bytes instead of", size.Message);
        Assert.Contains("checksum", checksum.Message);
        Assert.Empty(Directory.GetFiles(folder.Combine("b")));
        Assert.Empty(Directory.GetFiles(folder.Combine("c")));
    }

    /// <summary>Answers known URLs with their text, every other URL with 404.</summary>
    private sealed class Pages(Dictionary<string, string> pages) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(pages.TryGetValue(request.RequestUri!.AbsoluteUri, out var text)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(text, Encoding.UTF8) }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}
