using System.Text.Json;
using UEManager.Core.Mods;
using UEManager.Core.Storage;

namespace UEManager.Cli.Commands;

/// <summary>
/// `--cli ue4ss status|install|update|remove`: UE4SS itself, through the Core's own install and removal, over the same
/// manifest the app reads. status reads the data folder only, unless --check also asks the manifest.
/// </summary>
internal static class Ue4ssCommand
{
    public static int Run(CliArguments arguments)
    {
        // The command after "ue4ss" is taken out first, so each command checks only the options and words of its own.
        if (arguments.Positionals.Count != 1)
            throw new CliError("invalid-arguments", "ue4ss needs one command: status, install, update or remove.",
                details: ["Run --help for the usage."]);
        var subcommand = arguments.Positionals[0].ToLowerInvariant();
        arguments.Positionals.RemoveAt(0);
        return subcommand switch
        {
            "status" => Status(arguments),
            "install" => Install(arguments),
            "update" => Update(arguments),
            "remove" => Remove(arguments),
            _ => throw new CliError("invalid-arguments", $"Unknown ue4ss command '{subcommand}'.",
                details: ["Known commands: status, install, update, remove."]),
        };
    }

    /// <summary>
    /// The UE4SS in the game and the manager's record of it. Without --check nothing goes online: the record names the
    /// release the manager installed, and UE4SS's own files name one installed by hand.
    /// </summary>
    private static int Status(CliArguments arguments)
    {
        arguments.Allow(["--json", "--check"], ["--data-dir", "--game", "--game-root", "--manifest-url"]);
        arguments.NoPositionals();
        var context = CliContext.Open(arguments, needInstall: false, write: false);
        var warnings = new List<string>();
        var recordFile = context.Paths.Ue4ssRecordFile(context.GameId);
        var record = ReadRecord(recordFile, warnings);
        var runtime = context.Game is null ? null : Ue4ssRuntime.Detect(context.Game);
        if (context.Game is null)
            warnings.Add($"The {context.Profile.DisplayName} install folder was not found, so UE4SS cannot be looked for in the game.");

        var installed = runtime is { Installed: true };
        var version = record?.Version ?? runtime?.Version;
        var versionSource = record?.Version is not null ? "record" : runtime?.Version is not null ? "runtime" : null;
        var updateChecked = false;
        string? latestVersion = null;
        bool? updateAvailable = null;
        string? manifestUrl = null;
        if (arguments.Check)
        {
            var available = Ue4ssOnline.FindRelease(context, arguments.ManifestUrl);
            latestVersion = available.Release.Version;
            manifestUrl = available.ManifestUrl;
            updateChecked = true;
            // A UE4SS the manager has no record of, whose DLL is the manifest's build, is up to date: the app reads it so too.
            var installedVersion = record?.Version;
            if (installedVersion is null && installed && runtime!.IsBuild(available.Release.Version))
            {
                installedVersion = available.Release.Version;
                version = installedVersion;
                versionSource = "build";
            }
            updateAvailable = installed && Ue4ssManifest.IsUpdate(installedVersion, available.Release);
        }

        var payload = new Ue4ssStatusPayload(1, "ue4ss status", context.GameId, context.DataDir, recordFile,
            record is not null, installed, version, versionSource, record is not null, record?.Source,
            record is null || record.InstalledAt == default ? null : record.InstalledAt, record?.Files?.Count ?? 0,
            runtime?.Folder, runtime?.ModsPath, updateChecked, latestVersion, updateAvailable, manifestUrl, warnings);

        if (arguments.Json)
        {
            Output.Json(payload);
            return 0;
        }
        WriteStatus(payload);
        return 0;
    }

    private static void WriteStatus(Ue4ssStatusPayload payload)
    {
        Output.Line(payload.Installed
            ? $"UE4SS: installed {payload.Version ?? "(version unknown)"}{(payload.Managed ? "" : ", installed outside the app")}"
            : "UE4SS: not installed");
        if (payload.Installed)
        {
            Output.Line($"  Folder: {payload.Folder}");
            Output.Line($"  Mods: {payload.ModsPath}");
        }
        if (payload.Managed)
        {
            Output.Line($"  Source: {payload.Source ?? "unknown"}"
                + (payload.InstalledAt is { } at ? $", installed {Output.Time(at)}" : "")
                + $", {payload.FilesRecorded} file(s) recorded");
        }
        Output.Line($"  Record: {payload.RecordFile}{(payload.RecordExists ? "" : " (none)")}");
        if (!payload.UpdateChecked)
            Output.Line("  Update: not checked (pass --check to read the UE4SS manifest)");
        else if (!payload.Installed)
            Output.Line($"  Latest: {payload.LatestVersion}, from {payload.ManifestUrl ?? "the game profile's fixed package"}");
        else if (payload.UpdateAvailable == true)
            Output.Line($"  Update: available, {payload.LatestVersion}, from {payload.ManifestUrl ?? "the game profile's fixed package"}");
        else
            Output.Line($"  Update: none, {payload.Version ?? payload.LatestVersion} is the latest release");
        foreach (var warning in payload.Warnings)
            Output.Line($"Warning: {warning}");
    }

    /// <summary>
    /// Installs UE4SS from a release the player downloaded (--package, which works offline) or from the release the
    /// manifest names for this game. An install over an installed UE4SS is an update, as in the app.
    /// </summary>
    private static int Install(CliArguments arguments)
    {
        arguments.Allow(["--json"], ["--data-dir", "--game", "--game-root", "--package", "--version", "--staging", "--manifest-url"]);
        arguments.NoPositionals();
        var context = CliContext.Open(arguments, needInstall: true, write: true);
        var manager = context.OpenManager();
        var previousVersion = ReadRecord(context.Paths.Ue4ssRecordFile(context.GameId), [])?.Version;

        Ue4ssInstallPayload payload;
        if (arguments.Package is { } local)
        {
            payload = InstallPackage(context, manager, "ue4ss install", null, LocalPackage(local), arguments.VersionTag, previousVersion);
        }
        else
        {
            var available = Ue4ssOnline.FindRelease(context, arguments.ManifestUrl);
            if (arguments.VersionTag is { Length: > 0 } wanted
                && !wanted.Trim().Equals(available.Release.Version, StringComparison.OrdinalIgnoreCase))
            {
                throw new CliError("release-not-available",
                    $"The manifest names the UE4SS release '{available.Release.Version}', not '{wanted.Trim()}'.",
                    details:
                    [
                        "Pass --package <file> to install a release the manifest does not name.",
                        "Nothing was installed.",
                    ]);
            }
            using var package = DownloadPackage(context, arguments.Staging, available.Release);
            payload = InstallPackage(context, manager, "ue4ss install", available, package.File, ReleaseVersion(available.Release), previousVersion);
        }
        if (arguments.Json)
        {
            Output.Json(payload);
            return 0;
        }
        WriteInstall(payload);
        return 0;
    }

    /// <summary>
    /// Installs the release the manifest names only when it differs from the installed one; when it does not, the
    /// command says so and exits 0 without downloading anything.
    /// </summary>
    private static int Update(CliArguments arguments)
    {
        arguments.Allow(["--json"], ["--data-dir", "--game", "--game-root", "--staging", "--manifest-url"]);
        arguments.NoPositionals();
        var context = CliContext.Open(arguments, needInstall: true, write: true);
        var manager = context.OpenManager();
        var runtime = Ue4ssRuntime.Detect(context.Game!);
        if (!runtime.Installed)
        {
            throw new CliError("ue4ss-not-installed", $"UE4SS is not installed in {context.Profile.DisplayName}.",
                details: ["Run 'ue4ss install' first.", "Nothing was installed."]);
        }

        var previousVersion = ReadRecord(context.Paths.Ue4ssRecordFile(context.GameId), [])?.Version;
        var available = Ue4ssOnline.FindRelease(context, arguments.ManifestUrl);
        // As the app reads it: a UE4SS with no record whose DLL is the manifest's build is already up to date.
        var installedVersion = previousVersion;
        if (installedVersion is null && runtime.IsBuild(available.Release.Version))
            installedVersion = available.Release.Version;

        Ue4ssInstallPayload payload;
        if (Ue4ssManifest.IsUpdate(installedVersion, available.Release))
        {
            using var package = DownloadPackage(context, arguments.Staging, available.Release);
            payload = InstallPackage(context, manager, "ue4ss update", available, package.File,
                ReleaseVersion(available.Release), previousVersion);
        }
        else
        {
            payload = new Ue4ssInstallPayload(1, "ue4ss update", context.GameId, "up-to-date",
                ReleaseDto(available), null, installedVersion, installedVersion, runtime.Folder, 0, false, [], []);
        }

        if (arguments.Json)
        {
            Output.Json(payload);
            return 0;
        }
        WriteInstall(payload);
        return 0;
    }

    /// <summary>
    /// Removes UE4SS and everything inside its folder, and disables the mods that lived there (they stay in the
    /// library), as the app's own removal does. Without --yes it prints what that would remove and exits 2.
    /// </summary>
    private static int Remove(CliArguments arguments)
    {
        arguments.Allow(["--json", "--yes"], ["--data-dir", "--game", "--game-root"]);
        arguments.NoPositionals();
        var context = CliContext.Open(arguments, needInstall: true, write: arguments.Yes);
        var manager = context.OpenManager();
        var win64 = context.Game!.BinariesPath;
        var runtime = Ue4ssRuntime.Detect(context.Game);
        var record = ReadRecord(context.Paths.Ue4ssRecordFile(context.GameId), []);
        var paths = RemovablePaths(win64);
        // What RemoveUe4ss looks for: the 3.x folder or the 2.x UE4SS.dll, whoever put it there.
        var installed = runtime.Installed || paths.Count > 0;
        var disabledMods = manager.Mods
            .Where(mod => mod.Enabled && mod.Files.Any(file => file.Target == DeployTarget.Ue4ssMods))
            .Select(mod => new Ue4ssModRefDto(mod.Id, mod.Name))
            .ToList();
        var preview = new Ue4ssRemovePayload(1, "ue4ss remove", context.GameId, false, installed,
            record?.Version ?? runtime.Version, installed ? runtime.Folder : null, paths, disabledMods);

        if (!arguments.Yes)
        {
            if (arguments.Json)
                Output.Json(preview);
            else
                WriteRemovePreview(context.Profile.DisplayName, preview);
            Console.Error.WriteLine($"{Program.ToolName}: nothing was removed. Run the same command with --yes to remove UE4SS.");
            return Program.ConfirmationRequired;
        }

        Ue4ssRemoveResult result;
        try
        {
            result = manager.RemoveUe4ss();
        }
        catch (ModException error)
        {
            throw new CliError("ue4ss-not-removed", error.Message);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw new CliError("ue4ss-not-removed", $"UE4SS could not be removed: {error.Message}");
        }

        var payload = preview with { Confirmed = true, Installed = true };
        if (arguments.Json)
        {
            Output.Json(payload);
            return 0;
        }
        WriteRemoved(context.Profile.DisplayName, payload, result.DisabledMods);
        return 0;
    }

    /// <summary>The result of installing one package, as the command's JSON and text report it.</summary>
    private static Ue4ssInstallPayload InstallPackage(CliContext context, ModManager manager, string command,
        Ue4ssAvailableRelease? available, string package, string? version, string? previousVersion)
    {
        Ue4ssInstallResult result;
        try
        {
            result = manager.InstallUe4ss(package, version);
        }
        catch (ModException error)
        {
            throw new CliError("ue4ss-install-failed", error.Message);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw new CliError("ue4ss-install-failed", $"UE4SS could not be installed: {error.Message}");
        }
        var installed = ReadRecord(context.Paths.Ue4ssRecordFile(context.GameId), []);
        return new Ue4ssInstallPayload(1, command, context.GameId, result.Updated ? "updated" : "installed",
            // A downloaded package lives in a temporary folder, so the release names it; a local one is reported by path.
            available is null ? null : ReleaseDto(available), available is null ? package : null, installed?.Version,
            previousVersion, result.TargetFolder, result.FilesWritten, result.Updated, result.KeptFiles, result.RemovedFiles);
    }

    private static string LocalPackage(string path)
    {
        string full;
        try
        {
            full = Path.GetFullPath(path);
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new CliError("package-not-found", $"'{path}' is not a usable path: {error.Message}", details: ["Nothing was installed."]);
        }
        if (!File.Exists(full) && !Directory.Exists(full))
            throw new CliError("package-not-found", $"'{full}' does not exist.",
                details: ["--package takes a UE4SS release zip, 7z file or folder.", "Nothing was installed."]);
        return full;
    }

    private static Ue4ssPackage DownloadPackage(CliContext context, string? staging, Ue4ssRelease release)
    {
        string root;
        try
        {
            root = staging is null ? context.Paths.StagingDir : Path.GetFullPath(staging);
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new CliError("invalid-arguments", $"--staging '{staging}' is not a usable path: {error.Message}");
        }
        if (File.Exists(root))
            throw new CliError("invalid-arguments", $"--staging '{root}' is a file, not a folder.");
        return Ue4ssPackage.Download(release, root);
    }

    /// <summary>A release version as the manifest's entry spells it, or null when the entry names none.</summary>
    private static string? ReleaseVersion(Ue4ssRelease release) => string.IsNullOrWhiteSpace(release.Version) ? null : release.Version;

    private static Ue4ssReleaseDto ReleaseDto(Ue4ssAvailableRelease available) =>
        new(available.Release.Version, available.Release.FileName, available.Release.Url, available.Release.Sha256, available.ManifestUrl);

    /// <summary>
    /// The files and folders <see cref="ModManager.RemoveUe4ss"/> takes: UE4SS 3.x's folder and proxy DLL, and for a
    /// UE4SS 2.x the files it put straight into the exe folder, so the preview lists what the removal would touch.
    /// Empty when the removal would take nothing, as when only another tool's proxy DLL is there.
    /// </summary>
    private static IReadOnlyList<string> RemovablePaths(string win64)
    {
        var paths = new List<string>();
        var current = Directory.Exists(Path.Combine(win64, "ue4ss"));
        var legacy = File.Exists(Path.Combine(win64, "UE4SS.dll"));
        if (current)
            paths.Add(Path.Combine(win64, "ue4ss"));
        if ((current || legacy) && File.Exists(Path.Combine(win64, "dwmapi.dll")))
            paths.Add(Path.Combine(win64, "dwmapi.dll"));
        if (legacy)
        {
            var mods = Path.Combine(win64, "Mods");
            if (Directory.Exists(mods))
                paths.Add(mods);
            foreach (var name in new[] { "UE4SS.dll", "UE4SS.pdb", "UE4SS-settings.ini", "UE4SS.log", "xinput1_3.dll" })
            {
                var file = Path.Combine(win64, name);
                if (File.Exists(file))
                    paths.Add(file);
            }
        }
        return paths;
    }

    /// <summary>The manager's record of the UE4SS it installed; null when it installed none. An unreadable one is a warning.</summary>
    private static Ue4ssInstallRecord? ReadRecord(string file, List<string> warnings)
    {
        try
        {
            return JsonFile.Read<Ue4ssInstallRecord>(file);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            warnings.Add($"The UE4SS record '{file}' could not be read: {error.Message}");
            return null;
        }
    }

    private static void WriteInstall(Ue4ssInstallPayload payload)
    {
        if (payload.Action == "up-to-date")
        {
            Output.Line($"UE4SS is up to date ({payload.Version}).");
            return;
        }
        var verb = payload.Action == "updated" ? "Updated" : "Installed";
        Output.Line($"{verb} UE4SS {payload.Version} in {payload.TargetFolder}: {payload.FilesWritten} file(s) written.");
        if (payload.RemovedFiles.Count > 0)
            Output.Line($"{payload.RemovedFiles.Count} file(s) of the previous release were removed.");
        if (payload.KeptFiles.Count > 0)
            Output.Line($"{payload.KeptFiles.Count} file(s) the player changed were kept.");
    }

    private static void WriteRemovePreview(string displayName, Ue4ssRemovePayload payload)
    {
        if (!payload.Installed)
        {
            Output.Line($"UE4SS is not installed in {displayName}: nothing would be removed.");
            return;
        }
        Output.Line($"Would remove UE4SS from {displayName}:");
        foreach (var path in payload.Paths)
            Output.Line($"  {path}");
        if (payload.DisabledMods.Count == 0)
            Output.Line("No enabled mod in your list lives in UE4SS's folder.");
        else
            Output.Line($"{payload.DisabledMods.Count} enabled mod(s) would be disabled and stay in the library: "
                + string.Join(", ", payload.DisabledMods.Select(mod => mod.Name)));
        Output.Line("Settings the player changed are backed up first, as the Core does.");
    }

    private static void WriteRemoved(string displayName, Ue4ssRemovePayload payload, int disabledMods)
    {
        Output.Line($"Removed UE4SS from {displayName}: {payload.Paths.Count} item(s) left the game.");
        if (disabledMods > 0)
            Output.Line($"{disabledMods} mod(s) were disabled and stay in the library.");
    }
}
