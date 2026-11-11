using System.Text.Json;
using UEManager.Core.Mods;
using UEManager.Core.Storage;

namespace UEManager.Core.Games;

/// <summary>
/// Everything the manager knows about one game. Profiles are JSON files in
/// Games\Profiles, built into the assembly, so supporting a game or correcting
/// its folder layout is a data change rather than a code change.
/// </summary>
public sealed class GameProfile
{
    /// <summary>Placeholder in path templates for the Unreal project folder.</summary>
    public const string ProjectToken = "{Project}";

    private const string ResourcePrefix = "UEManager.Core.Games.Profiles.";

    public string Id { get; init; } = "";

    public string DisplayName { get; init; } = "";

    public string? Tagline { get; init; }

    /// <summary>The short line above the game's name on the Home page, such as "Ready your blade"; null for a plain one.</summary>
    public string? Motto { get; init; }

    public string? Developer { get; init; }

    /// <summary>
    /// The game is not out yet: its card in the game picker says "Coming soon", and only a build that allows it
    /// (AppInfo.CanChooseComingGames in the app) lets the player choose it.
    /// </summary>
    public bool ComingSoon { get; init; }

    /// <summary>
    /// The game's support is new and not checked on a real install yet, such as its exe or save folder: its card in
    /// the game picker and its Home page say Experimental.
    /// </summary>
    public bool Experimental { get; init; }

    /// <summary>
    /// The game is not offered yet: no card in the game picker or the tray menu, and a saved choice of it does not start
    /// it. Its support stays in the app, and a test run starts it with --game (see <see cref="GameChoice.Playable"/>).
    /// </summary>
    public bool Hidden { get; init; }

    /// <summary>Steam application id, used to find the install through Steam's library manifests.</summary>
    public string? SteamAppId { get; init; }

    /// <summary>Name the game has in the Epic Games Launcher's install manifests.</summary>
    public string? EpicDisplayName { get; init; }

    /// <summary>
    /// The Unreal project folder below the install folder: the one holding
    /// Content and Binaries. When it is missing or wrong, the folder is detected.
    /// </summary>
    public string? ProjectFolder { get; init; }

    /// <summary>Folder the game mounts .pak files from, relative to the install folder.</summary>
    public string PaksPath { get; init; } = "{Project}/Content/Paks";

    /// <summary>Folder packaged mods are deployed into, relative to the install folder.</summary>
    public string ModsPath { get; init; } = "{Project}/Content/Paks/~mods";

    /// <summary>Folder Blueprint mods are deployed into; UE4SS's BPModLoaderMod loads them from here.</summary>
    public string LogicModsPath { get; init; } = "{Project}/Content/Paks/LogicMods";

    /// <summary>
    /// Folder of the game's shipping executable, where UE4SS is installed. A <c>*</c> in one part stands
    /// for any folder, as in <c>platform/*</c> for a game with a folder per store: the one that holds the
    /// shipping executable wins, the store's own first.
    /// </summary>
    public string BinariesPath { get; init; } = "{Project}/Binaries/Win64";

    /// <summary>
    /// File name of the game's real executable in <see cref="BinariesPath"/>, the one UE4SS is loaded into.
    /// Null for Unreal's usual <c>&lt;Project&gt;-Win64-Shipping.exe</c>. When the named file is missing,
    /// as in another store's build, the largest executable of at least 50 MB there is taken.
    /// </summary>
    public string? ShippingExecutable { get; init; }

    /// <summary>Where "How to install UE4SS" sends the user; null for the official RE-UE4SS releases.</summary>
    public string? Ue4ssDownloadUrl { get; init; }

    /// <summary>A short hint shown with that link, such as which build to pick for this game.</summary>
    public string? Ue4ssNote { get; init; }

    /// <summary>
    /// The online manifest naming the current UE4SS release for this game, read at every
    /// start. It drives one-click install and update offers without a new app build. Copies
    /// of the same file on several hosts, tried in order (see <see cref="Mods.Ue4ssManifest"/>).
    /// </summary>
    public IReadOnlyList<string> Ue4ssManifestUrls { get; init; } = [];

    /// <summary>
    /// A fixed UE4SS release file (a zip) for one-click install when the manifest cannot be
    /// reached. Without either, the user downloads the file and picks it.
    /// </summary>
    public string? Ue4ssPackageUrl { get; init; }

    /// <summary>SHA-256 of that file. A download that does not match is refused.</summary>
    public string? Ue4ssPackageSha256 { get; init; }

    /// <summary>Started by "Launch game" when no store launcher is known, relative to the install folder.</summary>
    public string? Executable { get; init; }

    /// <summary>
    /// Arguments "Launch game" starts the game with, such as Black Myth: Wukong's -fileopenlog, which lets it load pak
    /// mods. A store's launcher cannot pass them on without asking the player, so the game's own exe starts with them.
    /// Null: the game starts through its store as usual.
    /// </summary>
    public string? LaunchArguments { get; init; }

    /// <summary>
    /// The folder holding the game's save files, for save backups: relative to the install folder, or a
    /// full path in which environment variables such as %LOCALAPPDATA% are expanded. Set it only when the
    /// folder is known for sure: without it, save backups are not offered for the game.
    /// </summary>
    public string? SaveFolder { get; init; }

    /// <summary>
    /// The address of a mod's page on the mod site, with <c>{id}</c> for the mod's number there. Downloads from
    /// the site keep that number in their file name (see <see cref="ModNaming.SiteModId"/>). Null: no mod page links.
    /// </summary>
    public string? ModPageUrl { get; init; }

    /// <summary>
    /// The mod site's public API, which the update check asks for the page version of the installed mods with a mod
    /// page (see <see cref="Updates.ModSiteVersions"/>). Null, or no <see cref="ModSiteGameId"/>: no update check.
    /// </summary>
    public string? ModSiteApiUrl { get; init; }

    /// <summary>The game's number on the mod site, which its API needs with every mod number.</summary>
    public string? ModSiteGameId { get; init; }

    /// <summary>The page of the mod installed from <paramref name="sourceFileName"/>; null when the name has no mod number.</summary>
    public string? ModPage(string? sourceFileName) =>
        ModPageUrl is { Length: > 0 } template && ModNaming.SiteModId(sourceFileName) is { } id
            ? template.Replace("{id}", id, StringComparison.Ordinal)
            : null;

    /// <summary>The profiles built into the application, sorted by display name.</summary>
    public static IReadOnlyList<GameProfile> BuiltIn()
    {
        var assembly = typeof(GameProfile).Assembly;
        return assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal)
                && name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            .Select(name =>
            {
                using var reader = new StreamReader(assembly.GetManifestResourceStream(name)!);
                return Parse(reader.ReadToEnd(), name[ResourcePrefix.Length..]);
            })
            .OrderBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>The built-in games the player can choose, by name: every one but the hidden ones.</summary>
    public static IReadOnlyList<GameProfile> Offered() => BuiltIn().Where(p => !p.Hidden).ToList();

    /// <summary>Reads a profile from JSON. Comments and trailing commas are allowed.</summary>
    public static GameProfile Parse(string json, string source)
    {
        GameProfile? profile;
        try
        {
            profile = JsonSerializer.Deserialize<GameProfile>(json, JsonFile.Options);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Game profile '{source}' is not valid JSON: {ex.Message}", ex);
        }
        if (profile is null || string.IsNullOrWhiteSpace(profile.Id) || string.IsNullOrWhiteSpace(profile.DisplayName))
            throw new InvalidDataException($"Game profile '{source}' needs an id and a displayName.");
        return profile;
    }

    /// <summary>
    /// Turns a profile path template into a full path below the install folder.
    /// Both slash styles are accepted, and an empty project folder simply drops out.
    /// </summary>
    public static string ResolvePath(string installRoot, string template, string? projectFolder)
    {
        var expanded = template.Replace(ProjectToken, projectFolder ?? "", StringComparison.OrdinalIgnoreCase);
        var parts = expanded.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        return Path.GetFullPath(Path.Combine([installRoot, .. parts]));
    }
}
