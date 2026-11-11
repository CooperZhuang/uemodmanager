using UEManager.Core.Games;

namespace UEManager.Core.Tests;

/// <summary>The folder report a player makes of a game the app does not know yet.</summary>
public class FolderReportTests
{
    private static readonly DateTime Made = new(2026, 11, 11, 12, 0, 0);

    [Fact]
    public void The_report_sums_up_an_unreal_game_and_lists_its_folders_and_programs()
    {
        using var folder = new TestFolder();
        folder.File(@"steamapps\appmanifest_123450.acf", "\"AppState\"\n{\n\t\"appid\"\t\t\"123450\"\n\t\"installdir\"\t\t\"My Game\"\n}");
        var game = folder.Combine("steamapps", "common", "My Game");
        folder.File(@"steamapps\common\My Game\MyGame.exe");
        folder.File(@"steamapps\common\My Game\steam_appid.txt", "123450\n");
        folder.File(@"steamapps\common\My Game\Engine\Binaries\ThirdParty\Steamworks\Steamv157\Win64\steam_api64.dll");
        folder.File(@"steamapps\common\My Game\MyGame\Content\Paks\MyGame-Windows.pak");
        folder.File(@"steamapps\common\My Game\MyGame\Content\Paks\MyGame-Windows.utoc");
        folder.File(@"steamapps\common\My Game\MyGame\Content\Paks\MyGame-Windows.ucas");
        folder.File(@"steamapps\common\My Game\MyGame\Content\Paks\global.utoc");
        folder.File(@"steamapps\common\My Game\MyGame\Content\Paks\~mods\Blue_P.pak");
        folder.File(@"steamapps\common\My Game\MyGame\Content\Movies\intro.bk2");
        folder.File(@"steamapps\common\My Game\MyGame\Binaries\Win64\MyGame-Win64-Shipping.exe");
        folder.File(@"steamapps\common\My Game\MyGame\Binaries\Win64\dwmapi.dll");
        folder.File(@"steamapps\common\My Game\MyGame\Binaries\Win64\ue4ss\UE4SS.dll");
        folder.File(@"steamapps\common\My Game\EasyAntiCheat\EasyAntiCheat_EOS_Setup.exe");
        // A large helper of the engine, as in Black Myth: Wukong's report: no game program.
        using (var helper = File.Create(folder.File(@"steamapps\common\My Game\Engine\Binaries\Win64\CrashReportClient.exe")))
            helper.SetLength(21L * 1024 * 1024);

        var report = FolderReport.Build(game, "UE MM 2.0.0", Made);
        var text = report.Text;

        Assert.True(report.LooksLikeUnreal);
        Assert.StartsWith("UE MM folder report", text);
        Assert.Contains("App: UE MM 2.0.0", text);
        Assert.Contains("Made: 2026-11-11 12:00", text);
        Assert.Contains("Unreal project folder: MyGame (Content\\Paks: 1 .pak, 2 .utoc, 1 .ucas)", text);
        Assert.Contains("~mods: yes, 1 file(s)", text);
        Assert.Contains("LogicMods: no", text);
        Assert.Contains("Engine folder: yes", text);
        Assert.Contains("Programs in the main folder: MyGame.exe", text);
        Assert.Contains(@"Likely game program: MyGame\Binaries\Win64\MyGame-Win64-Shipping.exe  4 B", text);
        Assert.DoesNotContain(@"Likely game program: Engine\", text);
        Assert.Contains("Loader DLLs next to it: dwmapi.dll  4 B", text);
        Assert.Contains(@"steam_api64.dll: Engine\Binaries\ThirdParty\Steamworks\Steamv157\Win64\steam_api64.dll  4 B", text);
        Assert.Contains("steam_appid.txt: 123450", text);
        Assert.Contains("Steam app id (Steam's list): 123450", text);
        Assert.Contains("Epic folder .egstore: no", text);
        Assert.Contains(@"UE4SS: MyGame\Binaries\Win64\ue4ss\UE4SS.dll", text);
        Assert.Contains("Anti-cheat folders: EasyAntiCheat", text);
        // The tree: folders first, programs with their sizes, other files counted per folder.
        Assert.Contains("\nMy Game/  (+1 other files)\n", text.ReplaceLineEndings("\n"));
        Assert.Contains("\n      Movies/  (+1 other files)\n", text.ReplaceLineEndings("\n"));
        Assert.Contains("\n        MyGame-Win64-Shipping.exe  4 B\n", text.ReplaceLineEndings("\n"));
        Assert.Contains(" folders, 4 .exe files, 3 .dll files", text);
    }

    [Fact]
    public void The_report_hides_the_user_name_and_account_numbers()
    {
        using var folder = new TestFolder();
        folder.File(@"Game\Game\Content\Paks\Game-Windows.pak");
        folder.File(@"Game\Game\Saved\SaveGames\76561198000000001\Slot1.sav");
        folder.File(@"Game\Game\Saved\SaveGames\0123456789abcdef0123456789abcdef\Slot1.sav");
        folder.File(@"Game\userdata\12345678\remote\x.dat");
        // A short number below userdata, as in Black Myth: Wukong's report.
        folder.File(@"Game\Engine\Steamworks\userdata\19627\x.dat");

        var text = FolderReport.Build(folder.Combine("Game"), "UE MM 2.0.0", Made).Text;

        Assert.DoesNotContain("76561198000000001", text);
        Assert.DoesNotContain("0123456789abcdef0123456789abcdef", text);
        Assert.DoesNotContain("12345678", text);
        Assert.DoesNotContain("19627", text);
        Assert.Contains("<steam id>/", text);
        Assert.Contains("<id>/", text);
        Assert.Contains("<number>/", text);
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (Path.GetTempPath().StartsWith(profile, StringComparison.OrdinalIgnoreCase))
        {
            Assert.Contains("Folder: %USERPROFILE%", text);
            Assert.DoesNotContain(profile, text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void A_folder_without_an_unreal_game_says_so()
    {
        using var folder = new TestFolder();
        folder.File(@"Photos\holiday.jpg");

        var report = FolderReport.Build(folder.Combine("Photos"), "UE MM 2.0.0", Made);

        Assert.False(report.LooksLikeUnreal);
        Assert.Contains("Unreal project folder: none found", report.Text);
        Assert.Contains("Likely game program: none", report.Text);
    }

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(1023, "1023 B")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(5L * 1024 * 1024, "5.0 MB")]
    [InlineData(3L * 1024 * 1024 * 1024, "3.0 GB")]
    public void Sizes_read_as_the_old_report_wrote_them(long bytes, string text) => Assert.Equal(text, FolderReport.Size(bytes));
}
