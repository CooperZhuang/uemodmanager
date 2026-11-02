using UEManager.Core.Mods;
using UEManager.UI.ViewModels;
using Xunit;

namespace UEManager.UI.Tests;

/// <summary>Finding lines in the UE4SS log on the UE4SS page.</summary>
public class Ue4ssLogSearchTests
{
    private static readonly Ue4ssLogLine Started = new("[12:40:01] Starting Lua mod 'PhantomTweaks'", Ue4ssLogLevel.Info);
    private static readonly Ue4ssLogLine Failed = new("[12:40:02] [Lua] Error: [PhantomTweaks] main.lua:3", Ue4ssLogLevel.Error);

    [Fact]
    public void The_search_finds_text_in_any_case_and_ignores_spaces_around_it()
    {
        Assert.True(Ue4ssLogViewModel.Shows(Started, false, " phantomtweaks "));
        Assert.False(Ue4ssLogViewModel.Shows(Started, false, "SpeakerHighlight"));
        Assert.True(Ue4ssLogViewModel.Shows(Started, false, "  "));
    }

    [Fact]
    public void The_search_and_only_errors_and_warnings_work_together()
    {
        Assert.False(Ue4ssLogViewModel.Shows(Started, true, "PhantomTweaks"));
        Assert.True(Ue4ssLogViewModel.Shows(Failed, true, "PhantomTweaks"));
        Assert.False(Ue4ssLogViewModel.Shows(Failed, true, "Starting"));
    }

    [Fact]
    public void Only_errors_and_warnings_leaves_out_known_lines()
    {
        var known = new Ue4ssLogLine("[12:40:00] [.ProcessConsoleExec.] Failed to add hook", Ue4ssLogLevel.Error, Known: true);

        Assert.False(Ue4ssLogViewModel.Shows(known, true, ""));
        Assert.True(Ue4ssLogViewModel.Shows(known, false, ""));
    }

    [Fact]
    public void A_mod_line_shows_for_its_folder_name()
    {
        var tagged = new Ue4ssLogLine("[12:40:03] [Lua] [Witch Time] 1.8 loaded.", Ue4ssLogLevel.Info, Ue4ssLogSource.Mod, "phantombladezero_witchtime");

        Assert.True(Ue4ssLogViewModel.Shows(tagged, false, "phantombladezero_witchtime"));
        Assert.False(Ue4ssLogViewModel.Shows(tagged, false, "PassiveRegen"));
    }
}
