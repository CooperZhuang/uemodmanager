using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace UEManager.Core.Games;

/// <summary>
/// A report of a game's install folder, for a game the app does not know yet: a short summary of what matters for adding
/// it, then every folder, and every program file (.exe, .dll) with its size; other files are only counted per folder. The
/// player shares it with us. It only reads: nothing is changed, and nothing is sent. The Windows user name and account
/// numbers in folder names are hidden. Like the help report, it stays English.
/// </summary>
/// <remarks>The tree is the one of the folder report script that players ran for Phantom Blade Zero before this view.</remarks>
public static partial class FolderReport
{
    /// <summary>A longer tree is cut: a game's folders fit well below it.</summary>
    public const int MaxTreeLines = 5000;

    /// <summary>A program at least this large is likely the game itself, whatever its name.</summary>
    private const long LargeProgram = 20L * 1024 * 1024;

    /// <summary>The walk stops after this many folders, so a wrong choice such as a whole drive ends soon.</summary>
    private const int MaxFolders = 20000;

    private static readonly HashSet<string> Listed = new(StringComparer.OrdinalIgnoreCase) { ".exe", ".dll" };

    /// <summary>Builds the report of a folder.</summary>
    /// <param name="root">The folder the player chose: the game's main folder.</param>
    /// <param name="app">The app and its version, such as "UE MM 2.0.0".</param>
    /// <param name="made">When the report is made.</param>
    public static FolderReportResult Build(string root, string app, DateTime made)
    {
        var folder = new DirectoryInfo(Path.GetFullPath(root));
        var walk = new Walk();
        var tree = new List<string> { Line(folder.Name + "/", OthersIn(folder)) };
        walk.Visit(folder, 1, tree);

        var projects = folder.EnumerateDirectories()
            .Where(d => !d.Name.Equals("Engine", StringComparison.OrdinalIgnoreCase) && Directory.Exists(Path.Combine(d.FullName, "Content", "Paks")))
            .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var text = new StringBuilder();
        text.AppendLine("UE MM folder report");
        text.AppendLine($"App: {app}");
        text.AppendLine($"Made: {made.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)}");
        text.AppendLine($"Folder: {folder.FullName}");
        text.AppendLine($"{walk.Folders} folders, {walk.Programs} .exe files, {walk.Libraries} .dll files");
        text.AppendLine();
        text.AppendLine("Summary");
        Summarize(folder, projects, walk, text);
        text.AppendLine();
        text.AppendLine("Folders");
        var cut = tree.Count - MaxTreeLines;
        foreach (var line in cut > 0 ? tree.Take(MaxTreeLines) : tree)
            text.AppendLine(line);
        if (cut > 0)
            text.AppendLine($"... {cut} more lines cut");
        if (walk.Stopped)
            text.AppendLine($"... the walk stopped after {MaxFolders} folders: is this the game's main folder?");
        return new FolderReportResult(Hide(text.ToString()), projects.Count > 0);
    }

    /// <summary>What we look for first when we add a game.</summary>
    private static void Summarize(DirectoryInfo root, List<DirectoryInfo> projects, Walk walk, StringBuilder text)
    {
        void Fact(string name, string value) => text.AppendLine($"  {name}: {value}");

        if (projects.Count == 0)
            Fact("Unreal project folder", "none found (no <folder>\\Content\\Paks below the chosen folder)");
        foreach (var project in projects)
        {
            var paks = new DirectoryInfo(Path.Combine(project.FullName, "Content", "Paks"));
            var counts = string.Join(", ", new[] { ".pak", ".utoc", ".ucas" }
                .Select(ext => $"{paks.EnumerateFiles("*" + ext).Count(f => f.Extension.Equals(ext, StringComparison.OrdinalIgnoreCase))} {ext}"));
            Fact("Unreal project folder", $"{project.Name} (Content\\Paks: {counts})");
            foreach (var mods in new[] { "~mods", "LogicMods" })
            {
                var place = new DirectoryInfo(Path.Combine(paks.FullName, mods));
                Fact($"  {mods}", place.Exists ? $"yes, {SafeCount(place)} file(s)" : "no");
            }
        }
        Fact("Engine folder", Directory.Exists(Path.Combine(root.FullName, "Engine")) ? "yes" : "no");
        Fact("Programs in the main folder", NamesOrNone(root.EnumerateFiles("*.exe").Select(f => f.Name)));
        // Not the engine's own helpers, such as CrashReportClient.exe: they are large too.
        var games = walk.Exes
            .Where(f => !Relative(root, f).StartsWith("Engine" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            .Where(f => f.Length >= LargeProgram || f.Name.EndsWith("-Shipping.exe", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(f => f.Length)
            .Take(10)
            .ToList();
        if (games.Count == 0)
            Fact("Likely game program", "none (no *-Shipping.exe, nothing of 20 MB or more)");
        foreach (var game in games)
            Fact("Likely game program", $"{Relative(root, game)}  {Size(game.Length)}{VersionOf(game)}");
        // DLLs that Windows loads into the game from its own folder: how mod loaders such as UE4SS get in.
        if (games.Count > 0)
            Fact("Loader DLLs next to it", NamesOrNone(LoaderDllsBeside(games[0]).Select(f => $"{f.Name}  {Size(f.Length)}")));
        // With its size: the real file is small, a much larger one has been replaced.
        Fact("steam_api64.dll", NamesOrNone(walk.Found("steam_api64.dll").Select(f => $"{Relative(root, f)}  {Size(f.Length)}")));
        var appIdFile = new FileInfo(Path.Combine(root.FullName, "steam_appid.txt"));
        if (appIdFile.Exists)
            Fact("steam_appid.txt", ReadSmall(appIdFile));
        if (SteamAppId(root) is { } appId)
            Fact("Steam app id (Steam's list)", appId);
        Fact("Epic folder .egstore", Directory.Exists(Path.Combine(root.FullName, ".egstore")) ? "yes" : "no");
        Fact("UE4SS", NamesOrNone(walk.Found("UE4SS.dll").Select(f => Relative(root, f))));
        Fact("Anti-cheat folders", NamesOrNone(walk.AntiCheat.Select(d => Relative(root, d))));
    }

    /// <summary>Names of DLLs that loaders put next to a game's exe, as Windows loads them from there first.</summary>
    private static readonly HashSet<string> LoaderDlls = new(StringComparer.OrdinalIgnoreCase)
    {
        "version.dll", "dwmapi.dll", "winmm.dll", "winhttp.dll", "dxgi.dll", "d3d11.dll", "d3d12.dll", "dinput8.dll",
        "xinput1_3.dll", "xinput1_4.dll", "xinput9_1_0.dll", "hid.dll", "dsound.dll",
    };

    private static IEnumerable<FileInfo> LoaderDllsBeside(FileInfo program)
    {
        try
        {
            return program.Directory?.EnumerateFiles("*.dll").Where(f => LoaderDlls.Contains(f.Name)).OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ToList() ?? [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>The app id in Steam's list of the library the folder is in, when it is in steamapps\common.</summary>
    private static string? SteamAppId(DirectoryInfo root)
    {
        if (root.Parent is not { Name: var common } parent || !common.Equals("common", StringComparison.OrdinalIgnoreCase)
            || parent.Parent is not { Name: var apps } steamapps || !apps.Equals("steamapps", StringComparison.OrdinalIgnoreCase))
            return null;
        try
        {
            foreach (var manifest in steamapps.EnumerateFiles("appmanifest_*.acf"))
            {
                var text = File.ReadAllText(manifest.FullName);
                var dir = InstallDir().Match(text);
                var id = AppId().Match(text);
                if (dir.Success && id.Success && dir.Groups[1].Value.Equals(root.Name, StringComparison.OrdinalIgnoreCase))
                    return id.Groups[1].Value;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
        return null;
    }

    /// <summary>The engine's version as the program states it, such as "++UE5+Release-5.4-CL-0".</summary>
    private static string VersionOf(FileInfo program)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(program.FullName);
            var parts = new[] { info.FileVersion, info.ProductVersion }.Where(v => !string.IsNullOrWhiteSpace(v)).Distinct().ToList();
            return parts.Count == 0 ? "" : $"  (version {string.Join(", ", parts)})";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "";
        }
    }

    private static string ReadSmall(FileInfo file)
    {
        try
        {
            return file.Length > 64 ? "(too long to show)" : File.ReadAllText(file.FullName).Trim();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "(cannot be read)";
        }
    }

    private static int SafeCount(DirectoryInfo folder)
    {
        try
        {
            return folder.EnumerateFiles("*", SearchOption.AllDirectories).Count();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private static string NamesOrNone(IEnumerable<string> names)
    {
        var list = names.ToList();
        return list.Count == 0 ? "none" : string.Join(", ", list);
    }

    private static string Relative(DirectoryInfo root, FileSystemInfo item) => Path.GetRelativePath(root.FullName, item.FullName);

    /// <summary>A folder's line, with the count of its other files.</summary>
    private static string Line(string name, int others) => others > 0 ? $"{name}  (+{others} other files)" : name;

    private static int OthersIn(DirectoryInfo folder)
    {
        try
        {
            return folder.EnumerateFiles().Count(f => !Listed.Contains(f.Extension));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    /// <summary>A size as the old report wrote it: whole bytes, else one decimal.</summary>
    internal static string Size(long bytes)
    {
        double size = bytes;
        foreach (var unit in new[] { "B", "KB", "MB", "GB" })
        {
            if (size < 1024 || unit == "GB")
                return unit == "B" ? $"{bytes} B" : string.Create(CultureInfo.InvariantCulture, $"{size:0.0} {unit}");
            size /= 1024;
        }
        return "";
    }

    /// <summary>
    /// The Windows user name, and numbers of accounts in folder names: Steam ids (765611...), long numbers such as the ids
    /// below Steam's userdata, and 32-digit hex ids such as Epic's.
    /// </summary>
    internal static string Hide(string text)
    {
        text = Privacy.HideUser(text);
        text = SteamId().Replace(text, "<steam id>");
        text = NumberFolder().Replace(text, "${indent}<number>/");
        return HexFolder().Replace(text, "${indent}<id>/");
    }

    [GeneratedRegex(@"7656119\d{10}")]
    private static partial Regex SteamId();

    [GeneratedRegex(@"^(?<indent>\s+)\d{6,}/", RegexOptions.Multiline)]
    private static partial Regex NumberFolder();

    [GeneratedRegex(@"^(?<indent>\s+)[0-9A-Fa-f]{32}/", RegexOptions.Multiline)]
    private static partial Regex HexFolder();

    [GeneratedRegex("\"installdir\"\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex InstallDir();

    [GeneratedRegex("\"appid\"\\s+\"(\\d+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex AppId();

    /// <summary>One walk through the folders: the tree's lines, and what the summary needs.</summary>
    private sealed class Walk
    {
        public int Folders { get; private set; }

        public int Programs { get; private set; }

        public int Libraries { get; private set; }

        public bool Stopped { get; private set; }

        public List<FileInfo> Exes { get; } = [];

        public List<FileInfo> Dlls { get; } = [];

        public List<DirectoryInfo> AntiCheat { get; } = [];

        public IEnumerable<FileInfo> Found(string name) => Dlls.Where(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        public void Visit(DirectoryInfo folder, int depth, List<string> lines)
        {
            var indent = new string(' ', depth * 2);
            List<FileSystemInfo> entries;
            try
            {
                entries = folder.EnumerateFileSystemInfos()
                    .OrderBy(e => e is FileInfo)
                    .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                lines.Add($"{indent}(cannot be read: {ex.Message})");
                return;
            }
            foreach (var entry in entries)
            {
                if (Stopped)
                    return;
                if (entry is DirectoryInfo dir)
                {
                    // Links to other places are not followed: they are not part of the game.
                    if (dir.Attributes.HasFlag(FileAttributes.ReparsePoint))
                        continue;
                    if (++Folders >= MaxFolders)
                        Stopped = true;
                    if (dir.Name.Contains("EasyAntiCheat", StringComparison.OrdinalIgnoreCase) || dir.Name.Contains("BattlEye", StringComparison.OrdinalIgnoreCase))
                        AntiCheat.Add(dir);
                    // Below a userdata folder, as Steam's, a folder of digits is an account's number, however short.
                    var name = folder.Name.Equals("userdata", StringComparison.OrdinalIgnoreCase) && dir.Name.All(char.IsAsciiDigit)
                        ? "<number>"
                        : dir.Name;
                    lines.Add(indent + Line(name + "/", OthersIn(dir)));
                    Visit(dir, depth + 1, lines);
                }
                else if (entry is FileInfo file && Listed.Contains(file.Extension))
                {
                    if (file.Extension.Equals(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        Programs++;
                        Exes.Add(file);
                    }
                    else
                    {
                        Libraries++;
                        Dlls.Add(file);
                    }
                    lines.Add($"{indent}{file.Name}  {Size(file.Length)}");
                }
            }
        }
    }
}

/// <param name="Text">The report.</param>
/// <param name="LooksLikeUnreal">A folder below the chosen one has Content\Paks, as an Unreal game has.</param>
public sealed record FolderReportResult(string Text, bool LooksLikeUnreal);
