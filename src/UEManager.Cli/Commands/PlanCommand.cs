namespace UEManager.Cli.Commands;

/// <summary>`--cli plan`: Inspect and Check the download and print the plan as JSON. Nothing in the game is changed.</summary>
internal static class PlanCommand
{
    public static int Run(CliArguments arguments)
    {
        arguments.Allow(["--json", "--all-parts", "--keep-both"],
            ["--data-dir", "--game", "--game-root", "--staging", "--part"]);
        var source = arguments.Single("a download (a .zip, .7z, .rar, .pak, .utoc, .ucas file, or a folder)");
        // plan writes nothing but its own staging: --staging keeps that out of a read-only data folder. The lock is still
        // taken, as the app holds the plan's mod list in memory.
        var context = CliContext.Open(arguments, needInstall: true, write: false);
        using var planned = PlannedInstall.Build(context, arguments, source);
        // The plan is JSON by design, so --json changes nothing here.
        Output.Json(planned.Plan);
        return 0;
    }
}
