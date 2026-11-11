using UEManager.Core.Games;
using UEManager.Core.Mods;

namespace UEManager.Core.Tests;

/// <summary>
/// A UE4SS the manager has no record of, as one put in by another copy of the app: its version resource and its log say
/// only "3.0.1 Beta", but its DLL holds the Git SHA it was built from, the end of a release version such as
/// v3.0.1-1195-g1ceefac8.
/// </summary>
public class Ue4ssGitShaTests
{
    [Fact]
    public void A_UE4SS_without_a_record_is_known_by_the_Git_SHA_in_its_DLL()
    {
        using var folder = new TestFolder();
        var game = GameLocator.Resolve(TestFolder.Profile(), folder.Game(), GameStore.Manual)!;
        folder.File(Path.Combine(game.BinariesPath, "ue4ss", "UE4SS.dll"), "MZ code ... 1ceefac8.jmap build_change_list ... UE4SS - v{}.{}.{}");

        var runtime = Ue4ssRuntime.Detect(game);

        Assert.True(runtime.IsBuild("v3.0.1-1195-g1ceefac8"));
        Assert.True(runtime.IsBuild(" v3.0.1-1195-G1CEEFAC8 "));
        Assert.False(runtime.IsBuild("v3.0.1-1188-gf885e8d2"));
        // A release with a suffix may change more than the DLL, such as a script: its update stays offered.
        Assert.False(runtime.IsBuild("v3.0.1-1195-g1ceefac8-r2"));
        Assert.False(runtime.IsBuild("3.0.1 Beta"));
        Assert.False(runtime.IsBuild(null));
    }

    [Fact]
    public void Without_UE4SS_no_build_matches()
    {
        using var folder = new TestFolder();
        var game = GameLocator.Resolve(TestFolder.Profile(), folder.Game(), GameStore.Manual)!;

        Assert.False(Ue4ssRuntime.Detect(game).IsBuild("v3.0.1-1195-g1ceefac8"));
    }
}
