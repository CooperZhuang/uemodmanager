using UEManager.Core;
using UEManager.Core.Mods;

namespace UEManager.Cli;

/// <summary>A UE4SS release the manifest names, and where it was read; the address is null for the profile's fixed package.</summary>
internal sealed record Ue4ssAvailableRelease(Ue4ssRelease Release, string? ManifestUrl);

/// <summary>
/// The online side of `ue4ss install`, `ue4ss update` and `ue4ss status --check`: the manifest that the game profile
/// names (or a test copy), read in order, then the release download through the Core. Every failure is a
/// <see cref="CliError"/> with text a player can act on.
/// </summary>
internal static class Ue4ssOnline
{
    /// <summary>As long as the app's own client; each manifest address keeps its own five seconds.</summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(3);

    /// <summary>
    /// The release to install: the first of the profile's manifest addresses that answers, else the profile's fixed
    /// package (<see cref="Ue4ssManifest.FixedRelease"/>), as the app's one-click install reads them.
    /// </summary>
    /// <param name="manifestUrl">A test copy of the manifest, read instead of the profile's addresses, as the app's own --ue4ss-manifest-url.</param>
    /// <exception cref="CliError">No manifest answered, or one answered without an entry for this game and the profile names no fixed package.</exception>
    public static Ue4ssAvailableRelease FindRelease(CliContext context, string? manifestUrl)
    {
        if (manifestUrl is not null)
            Log.Info($"UE4SS manifest from the command line: '{manifestUrl}'.");
        var urls = manifestUrl is null ? context.Profile.Ue4ssManifestUrls : [manifestUrl];
        Ue4ssManifestAnswer? answer = null;
        if (urls.Count > 0)
        {
            using var http = CreateClient();
            answer = Run(() => Ue4ssManifest.FetchAsync(http, urls, context.GameId));
            if (answer?.Release is { } named)
                return new Ue4ssAvailableRelease(named, answer.Url);
        }

        // The fallback release of the game profile, for a manifest that cannot be reached or names no entry.
        if (Ue4ssManifest.FixedRelease(context.Profile) is { } fixedRelease)
            return new Ue4ssAvailableRelease(fixedRelease, null);

        if (answer is not null)
        {
            throw new CliError("manifest-no-release",
                $"The UE4SS manifest at '{answer.Url}' names no release for {context.Profile.DisplayName} ({context.GameId}).",
                details: ["Nothing was installed.", "Pass --package <file> to install a downloaded UE4SS release."]);
        }
        if (urls.Count == 0)
        {
            throw new CliError("manifest-not-configured",
                $"No UE4SS manifest is set for {context.Profile.DisplayName}, and the game profile names no fixed release package.",
                details: ["Nothing was installed.", "Pass --package <file> to install a downloaded UE4SS release."]);
        }
        throw new CliError("manifest-unreachable", "The UE4SS manifest could not be read: none of its addresses answered.",
            details: [.. urls, "Check the connection, then try again.", "Or pass --package <file> to install a downloaded UE4SS release."]);
    }

    /// <summary>
    /// Downloads the release, checks its SHA-256 and installs it, with the Core's
    /// <see cref="ModManager.DownloadAndInstallUe4ssAsync"/>, as the app's one-click install does. The download's
    /// folder below <paramref name="downloadRoot"/> goes again, also when a step fails.
    /// </summary>
    /// <exception cref="CliError">The download failed or its checksum differs (download-failed), or the install failed.</exception>
    public static Ue4ssInstallResult DownloadAndInstall(ModManager manager, Ue4ssRelease release, string? downloadRoot)
    {
        var downloaded = false;
        try
        {
            using var http = CreateClient();
            return Run(() => manager.DownloadAndInstallUe4ssAsync(http, release, downloadRoot, downloaded: () => downloaded = true));
        }
        catch (ModException error)
        {
            throw downloaded
                ? new CliError("ue4ss-install-failed", error.Message)
                : new CliError("download-failed", error.Message, details: ["Nothing was installed."]);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw downloaded
                ? new CliError("ue4ss-install-failed", $"UE4SS could not be installed: {error.Message}")
                : new CliError("download-failed", $"The release could not be downloaded: {error.Message}", details: ["Nothing was installed."]);
        }
    }

    /// <summary>The app's kind of client, so the manifest and GitHub see the same program.</summary>
    public static HttpClient CreateClient() => HttpClients.Create($"UEModManager/{Program.ToolVersion}", Timeout);

    /// <summary>
    /// Runs an async call on one of the thread pool's threads: a command may run while the app's window owns the
    /// current thread, and its waits must not need that thread to continue.
    /// </summary>
    public static T Run<T>(Func<Task<T>> work) => Task.Run(work).GetAwaiter().GetResult();
}
