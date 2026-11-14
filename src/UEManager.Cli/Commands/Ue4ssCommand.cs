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
        arguments.Allow(["--json", "--check"], ["--data-dir", "--game", "--game-root", "--ue4ss-manifest-url"]);
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
        var newerInstalled = false;
        string? manifestUrl = null;
        if (arguments.Check)
        {
            var available = Ue4ssOnline.FindRelease(context, arguments.Ue4ssManifestUrl);
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
            // As ue4ss update has it: an older release than the installed one is no update.
            newerInstalled = installed && Ue4ssManifest.IsOlder(installedVersion, available.Release.Version);
            updateAvailable = installed && !newerInstalled && Ue4ssManifest.IsUpdate(installedVersion, available.Release);
        }

        var payload = new Ue4ssStatusPayload(1, "ue4ss status", context.GameId, context.DataDir, recordFile,
            record is not null, installed, version, versionSource, record is not null, record?.Source,
            record is null || record.InstalledAt == default ? null : record.InstalledAt, record?.Files?.Count ?? 0,
            runtime?.Folder, runtime?.ModsPath, updateChecked, latestVersion, updateAvailable, newerInstalled, manifestUrl, warnings);

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
        else if (payload.NewerInstalled)
            Output.Line($"  Update: none, the installed {payload.Version} is newer than the manifest's {payload.LatestVersion} (ue4ss install takes that one)");
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
        arguments.Allow(["--json"],
            ["--data-dir", "--game", "--game-root", "--package", "--ue4ss-version", "--staging", "--ue4ss-manifest-url"]);
        arguments.NoPositionals();
        var context = CliContext.Open(arguments, needInstall: true, write: true);
        var manager = context.OpenManager();
        var previousVersion = ReadRecord(context.Paths.Ue4ssRecordFile(context.GameId), [])?.Version;

        Ue4ssInstallPayload payload;
        if (arguments.Package is { } local)
        {
            var package = LocalPackage(local);
            payload = Installed(context, manager, "ue4ss install", null, package, previousVersion,
                () => manager.InstallUe4ss(package, arguments.Ue4ssVersion));
        }
        else
        {
            var available = Ue4ssOnline.FindRelease(context, arguments.Ue4ssManifestUrl);
            if (arguments.Ue4ssVersion is { Length: > 0 } wanted
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
            var downloadRoot = PlannedInstall.StagingFolder(arguments.Staging, context.Game!.RootPath);
            payload = Installed(context, manager, "ue4ss install", available, null, previousVersion,
                () => Ue4ssOnline.DownloadAndInstall(manager, available.Release, downloadRoot));
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
    /// Installs the release the manifest names when it differs from the installed one and is not older; otherwise the
    /// command says so and exits 0 without downloading anything. It never goes back: after a newer build was installed
    /// with --package, a script's update must not quietly take it away. The manifest may go back on purpose, so
    /// ue4ss install still takes its release, whatever it is.
    /// </summary>
    private static int Update(CliArguments arguments)
    {
        arguments.Allow(["--json"], ["--data-dir", "--game", "--game-root", "--staging", "--ue4ss-manifest-url"]);
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
        var available = Ue4ssOnline.FindRelease(context, arguments.Ue4ssManifestUrl);
        // As the app reads it: a UE4SS with no record whose DLL is the manifest's build is already up to date.
        var installedVersion = previousVersion;
        if (installedVersion is null && runtime.IsBuild(available.Release.Version))
            installedVersion = available.Release.Version;

        Ue4ssInstallPayload payload;
        var newerInstalled = Ue4ssManifest.IsOlder(installedVersion, available.Release.Version);
        if (!newerInstalled && Ue4ssManifest.IsUpdate(installedVersion, available.Release))
        {
            var downloadRoot = PlannedInstall.StagingFolder(arguments.Staging, context.Game!.RootPath);
            payload = Installed(context, manager, "ue4ss update", available, null, previousVersion,
                () => Ue4ssOnline.DownloadAndInstall(manager, available.Release, downloadRoot));
        }
        else
        {
            payload = new Ue4ssInstallPayload(1, "ue4ss update", context.GameId, newerInstalled ? "newer-installed" : "up-to-date",
                ReleaseDto(available), null, installedVersion, installedVersion, runtime.Folder, 0, false, [], [], []);
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
        var runtime = Ue4ssRuntime.Detect(context.Game!);
        var record = ReadRecord(context.Paths.Ue4ssRecordFile(context.GameId), []);
        // The Core's own list of what RemoveUe4ss takes, whoever put UE4SS there, and of the mods it disables.
        var removal = manager.PreviewUe4ssRemoval();
        var preview = new Ue4ssRemovePayload(1, "ue4ss remove", context.GameId, false, removal.Installed,
            record?.Version ?? runtime.Version, removal.Installed ? runtime.Folder : null, removal.Paths,
            [.. removal.DisabledMods.Select(mod => new Ue4ssModRefDto(mod.Id, mod.Name))]);

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

    /// <summary>
    /// Runs one install of UE4SS, from a package on this PC or a download, and reports it as the command's JSON and text
    /// do, with the app's warning for installed C++ mods: each is made for one UE4SS build.
    /// </summary>
    private static Ue4ssInstallPayload Installed(CliContext context, ModManager manager, string command,
        Ue4ssAvailableRelease? available, string? package, string? previousVersion, Func<Ue4ssInstallResult> install)
    {
        var warnings = CppModWarning(manager.Mods) is { } warning ? new List<string> { warning } : [];
        Ue4ssInstallResult result;
        try
        {
            result = install();
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
            available is null ? null : ReleaseDto(available), package, installed?.Version,
            previousVersion, result.TargetFolder, result.FilesWritten, result.Updated, result.KeptFiles, result.RemovedFiles, warnings);
    }

    /// <summary>The app's note before a change of UE4SS (MainViewModel.Ue4ssChangeWarning), in English; null without C++ mods.</summary>
    private static string? CppModWarning(IReadOnlyList<ModRecord> mods)
    {
        var names = ModTypes.BuiltForOneUe4ss(mods).Select(mod => $"\"{mod.Name}\"").ToList();
        return names.Count switch
        {
            0 => null,
            1 => $"{names[0]} is a C++ mod. A C++ mod is made for one UE4SS build, and with another build it may not load until its author updates it. Its mod page names the UE4SS build it needs.",
            _ => $"{string.Join(", ", names)} are C++ mods. A C++ mod is made for one UE4SS build, and with another build it may not load until its author updates it. Each mod's page names the UE4SS build it needs.",
        };
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

    private static Ue4ssReleaseDto ReleaseDto(Ue4ssAvailableRelease available) =>
        new(available.Release.Version, available.Release.FileName, available.Release.Url, available.Release.Sha256, available.ManifestUrl);

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
        if (payload.Action == "newer-installed")
        {
            Output.Line($"The installed UE4SS {payload.Version} is newer than the manifest's {payload.Release?.Version}: "
                + "ue4ss update never goes back. Run ue4ss install to take the manifest's release.");
            return;
        }
        var verb = payload.Action == "updated" ? "Updated" : "Installed";
        Output.Line($"{verb} UE4SS {payload.Version} in {payload.TargetFolder}: {payload.FilesWritten} file(s) written.");
        if (payload.RemovedFiles.Count > 0)
            Output.Line($"{payload.RemovedFiles.Count} file(s) of the previous release were removed.");
        if (payload.KeptFiles.Count > 0)
            Output.Line($"{payload.KeptFiles.Count} file(s) the player changed were kept.");
        foreach (var warning in payload.Warnings)
            Output.Line($"Warning: {warning}");
    }

    private static void WriteRemovePreview(string displayName, Ue4ssRemovePayload payload)
    {
        if (!payload.Installed)
        {
            Output.Line($"UE4SS is not installed in {displayName}: nothing would be removed.");
            return;
        }
        // The app's question before its removal says the same.
        Output.Line($"Would remove UE4SS from {displayName} completely: dwmapi.dll and the whole ue4ss folder, with UE4SS's "
            + "settings and every mod inside it, also mods installed by hand:");
        foreach (var path in payload.Paths)
            Output.Line($"  {path}");
        if (payload.DisabledMods.Count == 0)
            Output.Line("No enabled mod in your list has files in UE4SS's Mods folder.");
        else
            Output.Line($"{payload.DisabledMods.Count} enabled mod(s) in your list would be disabled: "
                + string.Join(", ", payload.DisabledMods.Select(mod => mod.Name))
                + ". Their copies stay in the library, so you can enable them again after installing UE4SS.");
        Output.Line("Close the game first.");
    }

    private static void WriteRemoved(string displayName, Ue4ssRemovePayload payload, int disabledMods)
    {
        Output.Line($"Removed UE4SS from {displayName}: {payload.Paths.Count} item(s) left the game.");
        if (disabledMods > 0)
            Output.Line($"{disabledMods} mod(s) were disabled and stay in the library.");
    }
}
