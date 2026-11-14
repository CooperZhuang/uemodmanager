namespace UEManager.Cli;

/// <summary>
/// The command line tool's way in from the app: "UEModManager.exe --cli list" runs one command, prints its result and
/// ends with its exit code, without a window. Any other start is the app's own.
/// </summary>
public static class CommandLine
{
    /// <summary>The first argument that makes a start a command line one.</summary>
    public const string Switch = "--cli";

    /// <summary>The start is a command line one: its first argument is <see cref="Switch"/>.</summary>
    public static bool Wants(IReadOnlyList<string> args) =>
        args.Count > 0 && args[0].Equals(Switch, StringComparison.OrdinalIgnoreCase);

    /// <summary>Runs the command after <see cref="Switch"/>.</summary>
    /// <returns>The exit code: 0 success, 1 error, 2 confirmation required.</returns>
    public static int Run(IReadOnlyList<string> args) => Program.Main([.. args.Skip(1)]);
}
