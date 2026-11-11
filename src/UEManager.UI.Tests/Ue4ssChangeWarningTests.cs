using UEManager.Core.Mods;
using UEManager.UI.ViewModels;
using Xunit;

namespace UEManager.UI.Tests;

/// <summary>Before UE4SS is installed or replaced, the app names the C++ mods: each is built for one UE4SS build.</summary>
[Collection("Language")]
public class Ue4ssChangeWarningTests
{
    private static ModRecord Mod(string name, params (DeployTarget Target, string Path)[] files) => new()
    {
        Id = name,
        Name = name,
        Files = [.. files.Select(f => new ModFileRecord { Target = f.Target, FileName = f.Path })],
    };

    private static readonly ModRecord Cpp = Mod("Custom Shell System",
        (DeployTarget.Ue4ssMods, "CustomShellSystem/dlls/main.dll"), (DeployTarget.Ue4ssMods, "CustomShellSystem/enabled.txt"));

    private static readonly ModRecord Lua = Mod("Mods Menu", (DeployTarget.Ue4ssMods, "ModsMenu/Scripts/main.lua"));

    private static readonly ModRecord Pak = Mod("Shared Assets", (DeployTarget.Mods, "CSS_SharedAssets_P.pak"));

    private static readonly ModRecord Blueprint = Mod("Blueprint Mod", (DeployTarget.LogicMods, "BPMod.pak"));

    [Fact]
    public void Without_a_cpp_mod_nothing_is_asked()
    {
        Localizer.Current.SetLanguage("en");
        Assert.Null(MainViewModel.Ue4ssChangeWarning([]));
        Assert.Null(MainViewModel.Ue4ssChangeWarning([Lua, Pak, Blueprint]));
    }

    [Fact]
    public void Each_cpp_mod_is_named_and_only_those()
    {
        Localizer.Current.SetLanguage("en");
        var one = MainViewModel.Ue4ssChangeWarning([Lua, Cpp, Pak]);
        Assert.StartsWith("\"Custom Shell System\" is a C++ mod.", one);
        Assert.DoesNotContain("Mods Menu", one);

        var both = MainViewModel.Ue4ssChangeWarning([Cpp, Lua, Mod("Debugger", (DeployTarget.Ue4ssMods, "Debugger/dlls/main.dll"))]);
        Assert.StartsWith("\"Custom Shell System\", \"Debugger\" are C++ mods.", both);
    }

    [Fact]
    public void A_dll_counts_only_in_a_dlls_folder_of_ue4ss_mods()
    {
        Assert.True(ModTypes.HasUe4ssDll([(DeployTarget.Ue4ssMods, "A/dlls/main.dll")]));
        Assert.False(ModTypes.HasUe4ssDll([(DeployTarget.Ue4ssMods, "A/Scripts/main.lua")]));
        Assert.False(ModTypes.HasUe4ssDll([(DeployTarget.ExeFolder, "dlls/dxgi.dll")]));
        Assert.Equal("UE4SS DLL", ModTypes.Label([(DeployTarget.Ue4ssMods, "A/dlls/main.dll")]));
    }
}
