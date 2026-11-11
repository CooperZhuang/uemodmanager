using System.Diagnostics;

namespace UEManager.Core.Games;

public enum GameStore
{
    Manual,
    Steam,
    Epic,
}

/// <summary>A located installation of a game, with every profile path resolved.</summary>
/// <param name="BinariesPath">Folder of the shipping executable, where UE4SS is installed.</param>
public sealed record GameInstall(
    GameProfile Profile,
    string RootPath,
    string? ProjectFolder,
    string PaksPath,
    string ModsPath,
    string BinariesPath,
    GameStore Store,
    string? BuildId = null,
    string? LaunchUri = null)
{
    /// <summary>Helper programs Unreal games ship next to the real executable.</summary>
    private static readonly string[] HelperExecutables = ["UnrealCEFSubProcess", "CrashReportClient", "UEPrereqSetup"];

    /// <summary>Folder for Blueprint mods, which UE4SS's BPModLoaderMod loads.</summary>
    public string LogicModsPath => GameProfile.ResolvePath(RootPath, Profile.LogicModsPath, ProjectFolder);

    /// <summary>The game's real executable in <see cref="BinariesPath"/>, such as Game-Win64-Shipping.exe.</summary>
    public string? FindShippingExecutable() => FindShippingExecutable(Profile, BinariesPath);

    /// <summary>
    /// A game's real executable is hundreds of MB, its helpers such as a crash reporter a few MB.
    /// Only a minimum: the size changes with every game update.
    /// </summary>
    internal const long MinimumGameExecutableSize = 50L * 1024 * 1024;

    /// <summary>
    /// The profile's shipping executable in a folder, else the largest executable of at least
    /// <see cref="MinimumGameExecutableSize"/>: another store's build may name it differently. Without a name
    /// in the profile, the usual Unreal one, else any.
    /// </summary>
    internal static string? FindShippingExecutable(GameProfile profile, string folder)
    {
        try
        {
            if (!Directory.Exists(folder))
                return null;
            if (!string.IsNullOrWhiteSpace(profile.ShippingExecutable))
            {
                var named = Path.Combine(folder, profile.ShippingExecutable);
                if (File.Exists(named))
                    return named;
                return new DirectoryInfo(folder).EnumerateFiles("*.exe")
                    .Where(file => file.Length >= MinimumGameExecutableSize)
                    .MaxBy(file => file.Length)?.FullName;
            }
            return Directory.EnumerateFiles(folder, "*-Win64-Shipping.exe").FirstOrDefault()
                ?? Directory.EnumerateFiles(folder, "*.exe").FirstOrDefault();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// The executable "Launch game" starts when no store launcher is known: the profile's
    /// choice, else the first executable in the install folder, else the shipping executable.
    /// </summary>
    public string? FindExecutable()
    {
        if (!string.IsNullOrWhiteSpace(Profile.Executable))
        {
            var configured = GameProfile.ResolvePath(RootPath, Profile.Executable, ProjectFolder);
            if (File.Exists(configured))
                return configured;
        }
        return Directory.EnumerateFiles(RootPath, "*.exe")
            .FirstOrDefault(path => !HelperExecutables.Any(helper =>
                Path.GetFileNameWithoutExtension(path).StartsWith(helper, StringComparison.OrdinalIgnoreCase)))
            ?? FindShippingExecutable();
    }

    /// <summary>
    /// Starts the game through its store when known, so DRM and overlays work as usual. With start arguments (the player's
    /// own or the game's recommended ones, see <see cref="LaunchOptions"/>), it starts from its own exe with them.
    /// </summary>
    public void Launch(string? arguments)
    {
        var (target, startArguments, folder) = LaunchCommand(arguments);
        Process.Start(new ProcessStartInfo(target, startArguments ?? "")
        {
            UseShellExecute = true,
            WorkingDirectory = folder,
        });
    }

    /// <summary>What <see cref="Launch"/> starts with the profile's recommended arguments.</summary>
    internal (string Target, string? Arguments, string? Folder) LaunchCommand() => LaunchCommand(Profile.LaunchArguments);

    /// <summary>What <see cref="Launch"/> starts: a store's address, or an exe with its arguments and its folder.</summary>
    internal (string Target, string? Arguments, string? Folder) LaunchCommand(string? arguments)
    {
        if (LaunchOptions.Clean(arguments) is { } startArguments && (FindShippingExecutable() ?? FindExecutable()) is { } exe)
            return (exe, startArguments, Path.GetDirectoryName(exe));
        var target = LaunchUri ?? FindExecutable()
            ?? throw new InvalidOperationException("No game executable was found in the install folder.");
        return (target, null, LaunchUri is null ? Path.GetDirectoryName(target) : null);
    }
}
