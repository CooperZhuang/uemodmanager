namespace UEManager.Cli.Tests;

/// <summary>
/// The command line tool's tests take over the process's console output, so they run one at a time, whatever class
/// they are in.
/// </summary>
[CollectionDefinition(Name)]
public sealed class CliConsole
{
    public const string Name = "cli-console";
}
