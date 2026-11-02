using UEManager.Core.Mods;

namespace UEManager.Core.Tests;

public class Ue4ssLogTests : IDisposable
{
    private readonly TestFolder folder = new();

    public void Dispose() => folder.Dispose();

    [Fact]
    public void Marks_errors_and_warnings_and_names_the_mods_in_errors()
    {
        var path = folder.File("UE4SS.log", string.Join(Environment.NewLine,
            "[12:00:00.001] UE4SS - v3.0.1 Beta #0 - Git SHA #d935b5b",
            "[12:00:01.002] Starting Lua mod 'Witch'",
            "[12:00:01.003] [Lua] Error: [Witch] main.lua:12: attempt to index a nil value",
            "[12:00:01.004] Warning: setting 'GuiConsoleEnabled' is deprecated",
            "[12:00:01.005] Mod 'DashEnhanced-x2' failed to start",
            "[12:00:01.006] Starting Lua mod 'Witchcraft'"));

        var log = Ue4ssLog.Read(path)!;

        Assert.Equal(6, log.Lines.Count);
        Assert.Equal(2, log.ErrorCount);
        Assert.Equal(1, log.WarningCount);
        Assert.Equal(Ue4ssLogLevel.Info, log.Lines[1].Level);
        Assert.Equal(["DashEnhanced-x2", "Witch"], log.ModsNamedInErrors(["Witch", "Witchcraft", "DashEnhanced-x2", "DashEnhanced"]));
    }

    [Fact]
    public void Sorts_the_lines_into_UE4SS_mods_and_unnamed_and_leaves_known_lines_out_of_the_errors()
    {
        var path = folder.File("UE4SS.log", string.Join(Environment.NewLine,
            "[2026-11-01 19:56:19.4999718] UE4SS - v3.0.1 Beta #0 - Git SHA #1ceefac8",
            "[2026-11-01 19:56:30.3797355] [.ProcessConsoleExec.] Failed to add hook, detour installation likely failed!",
            "[2026-11-01 19:56:30.3831207] Starting Lua mod 'CheatManagerEnablerMod'",
            "[2026-11-01 19:56:30.3833198] [Lua] Exposing size of 'class RC::Unreal::FObjectProperty' as 0x8",
            "[2026-11-01 19:56:30.3833610] [Lua] Failed to expose size of 'class RC::Unreal::FObjectPtrProperty', internal value invalid, manually set this value in your Lua mod if you intend to use it.",
            "[2026-11-01 19:56:30.3990872] [Lua] [BPModLoaderMod] Mods/BPModLoaderMod/load_order.txt not present or no matching mods, loading all BP mods in random order.",
            "[2026-11-01 19:56:30.4033858] Mod 'phantombladezero_witchtime' has enabled.txt, starting mod.",
            "[2026-11-01 19:56:30.4043441] [Lua] [Witch Time] 1.8 loaded.",
            "[2026-11-01 19:56:30.4127016] Event loop start",
            "[2026-11-01 19:56:41.6031088] [Lua] [Witch Time] Error: could not hook OnEvadeDamage",
            "stack traceback: main.lua:40",
            "[2026-11-01 19:56:41.6654643] [Lua] [ConsoleEnabler] ConsoleKey[1]: Tilde",
            "[2026-11-01 19:56:41.6809706] [Lua] [Auto Sheathe] Settings: Enable=true",
            "[2026-11-01 19:56:41.7000000] [Lua] hello",
            @"[2026-11-01 19:56:42.0000000] [Lua] Error: Mods\PassiveRegen\Scripts\main.lua:10: attempt to call a nil value"));

        var log = Ue4ssLog.Read(path, ["CheatManagerEnablerMod", "ConsoleEnablerMod", "phantombladezero_autosheathe", "PassiveRegen"])!;

        Assert.Equal(
            [Ue4ssLogSource.Ue4ss, Ue4ssLogSource.Ue4ss, Ue4ssLogSource.Ue4ss, Ue4ssLogSource.Ue4ss, Ue4ssLogSource.Ue4ss, Ue4ssLogSource.Ue4ss,
             Ue4ssLogSource.Mod, Ue4ssLogSource.Mod, Ue4ssLogSource.Ue4ss, Ue4ssLogSource.Mod, Ue4ssLogSource.Mod,
             Ue4ssLogSource.Ue4ss, Ue4ssLogSource.Mod, Ue4ssLogSource.Unnamed, Ue4ssLogSource.Mod],
            log.Lines.Select(l => l.Source));
        Assert.Equal([1, 4], log.Lines.Select((l, i) => (l, i)).Where(x => x.l.Known).Select(x => x.i));
        Assert.Equal("phantombladezero_witchtime", log.Lines[10].Mod);
        Assert.Equal("phantombladezero_autosheathe", log.Lines[12].Mod);
        Assert.Equal("ConsoleEnablerMod", log.Lines[11].Mod);
        Assert.Equal(2, log.ErrorCount);
        Assert.Equal(2, log.KnownCount);
        Assert.Equal(["PassiveRegen", "phantombladezero_witchtime"], log.ModsNamedInErrors(["phantombladezero_witchtime", "PassiveRegen", "BPModLoaderMod"]));
    }

    [Fact]
    public void A_tag_that_fits_two_mods_stays_unnamed()
    {
        var path = folder.File("UE4SS.log", string.Join(Environment.NewLine,
            "[12:00:00.001] Event loop start",
            "[12:00:01.002] [Lua] [Sheathe] done"));

        var log = Ue4ssLog.Read(path, ["autosheathe", "sheathe_plus"])!;

        Assert.Equal(Ue4ssLogSource.Unnamed, log.Lines[1].Source);
        Assert.Null(log.Lines[1].Mod);
    }

    [Fact]
    public void The_built_in_rules_name_known_lines_and_UE4SS_mods()
    {
        var rules = Ue4ssLogRules.BuiltIn;

        Assert.NotEmpty(rules.Known);
        Assert.All(rules.Known, known => Assert.False(string.IsNullOrWhiteSpace(known.Text)));
        Assert.True(rules.IsUe4ssMod("bpmodloadermod"));
        Assert.False(rules.IsUe4ssMod("phantombladezero_witchtime"));
    }

    [Fact]
    public void Keeps_the_end_of_a_long_log_and_reads_it_while_the_game_writes()
    {
        var path = folder.Combine("UE4SS.log");
        using (var game = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read))
        using (var writer = new StreamWriter(game))
        {
            for (var i = 1; i <= Ue4ssLog.MaxLines + 50; i++)
                writer.WriteLine($"line {i}");
            writer.Flush();

            var log = Ue4ssLog.Read(path)!;

            Assert.True(log.Truncated);
            Assert.Equal(Ue4ssLog.MaxLines, log.Lines.Count);
            Assert.Equal($"line {Ue4ssLog.MaxLines + 50}", log.Lines[^1].Text);
        }
    }

    [Fact]
    public void No_log_yet_is_null() => Assert.Null(Ue4ssLog.Read(folder.Combine("UE4SS.log")));
}
