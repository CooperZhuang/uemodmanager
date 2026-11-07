using UEManager.Core.Games;
using UEManager.Core.Mods;

namespace UEManager.Core.Tests;

/// <summary>The mod's number on the mod site, read from its download's file name, and the page address made from it.</summary>
public class ModPageTests
{
    [Theory]
    [InlineData("Sharp Blades-12-1-2-1730000000.zip", "12")]
    [InlineData("Travel Anywhere 61 1.0.1 2026-10-29T13-42Z LbtEo64mS.zip", "61")]
    [InlineData("PhantomTweaks 1.0.0 44 1.0.0 2026-10-29T09-10Z iFHyWBRqE.zip", "44")]
    [InlineData("PBZ SpeakerHighlight PVLW 0.1.0 27 1 2026-10-29T06-37Z 5qSwPXtlu.zip", "27")]
    [InlineData("PBZTuner_V1.0.2_21_3_2026-10-29T12-51Z_HOPKBToED.zip", "21")]
    [InlineData("Mods Menu 149 0.7.2 2026-11-05T10-00Z AbC123xyz.7z", "149")]
    [InlineData(@"C:\Downloads\Mods Menu 149 0.7.2 2026-11-05T10-00Z AbC123xyz.rar", "149")]
    [InlineData("PBZMapMarkers 1.8.2 Fog Teleport 134 1.8.2 2026-11-05T07-30Z YQ3oJLMF9(1).zip", "134")]
    [InlineData("Travel Anywhere 61 1.0.1 2026-10-29T13-42Z LbtEo64mS (2).zip", "61")]
    [InlineData("Sharp Blades-12-1-2-1730000000 (1).zip", "12")]
    [InlineData("Travel Anywhere 61 1.0.1 2026-10-29T13-42Z LbtEo64mS (1)", "61")]
    public void A_download_from_the_mod_site_has_the_mods_number(string fileName, string id)
    {
        Assert.Equal(id, ModNaming.SiteModId(fileName));
    }

    [Theory]
    [InlineData("Travel Anywhere 61 1.0.1 2026-10-29T13-42Z LbtEo64mS")]
    [InlineData(@"C:\Mods\Travel Anywhere 61 1.0.1 2026-10-29T13-42Z LbtEo64mS\")]
    [InlineData("Sharp Blades-12-1-2-1730000000")]
    public void A_folder_unpacked_under_that_name_has_it_too(string folderName)
    {
        Assert.NotNull(ModNaming.SiteModId(folderName));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Travel Anywhere.zip")]
    [InlineData("Sharp Blades v1.3.0.zip")]
    [InlineData("Mod 2.zip")]
    [InlineData("KintaroCdoDashTest_0.2.zip")]
    [InlineData("PBZModLoader-2.1.0-win64.zip")]
    [InlineData("phantombladezero_cppbypass_1.1_experimental.zip")]
    [InlineData("Sharp_Blades_P.pak")]
    [InlineData("Outfit (2).zip")]
    public void Any_other_name_has_none(string? fileName)
    {
        Assert.Null(ModNaming.SiteModId(fileName));
    }

    [Fact]
    public void The_profile_makes_the_page_address_from_the_number()
    {
        var pbz = Assert.Single(GameProfile.BuiltIn(), p => p.Id == "phantom-blade-zero");

        Assert.Equal("https://www.nexusmods.com/phantombladezero/mods/149", pbz.ModPage("Mods Menu 149 0.7.2 2026-11-05T10-00Z AbC123xyz.zip"));
        Assert.Null(pbz.ModPage("PBZModLoader-2.1.0-win64.zip"));
        Assert.Null(pbz.ModPage(null));
    }

    [Fact]
    public void A_profile_without_a_page_address_has_no_links()
    {
        var profile = new GameProfile { Id = "test", DisplayName = "Test" };

        Assert.Null(profile.ModPage("Travel Anywhere 61 1.0.1 2026-10-29T13-42Z LbtEo64mS.zip"));
    }
}
