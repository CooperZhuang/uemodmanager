using UEManager.Core;
using UEManager.Core.Mods;

namespace UEManager.Cli;

/// <summary>A UE4SS release the manifest names, and where it was read; the address is null for the profile's fixed package.</summary>
internal sealed record Ue4ssAvailableRelease(Ue4ssRelease Release, string? ManifestUrl);

/// <summary>
/// A UE4SS release package downloaded into a temporary folder of its own, which is removed when the command ends, also
/// when it fails, so a wrong or half-used download never stays behind.
/// </summary>
internal sealed class Ue4ssPackage : IDisposable
{
    private readonly string folder;

    private Ue4ssPackage(string file, string folder)
    {
        File = file;
        this.folder = folder;
    }

    public string File { get; }

    /// <summary>Downloads the release into a temporary folder below <paramref name="root"/>.</summary>
    /// <exception cref="CliError">The download failed or its checksum differs; nothing is left behind.</exception>
    public static Ue4ssPackage Download(Ue4ssRelease release, string root)
    {
        var folder = "";
        try
        {
            // The data folder's staging folder is emptied by the app at every start, so a download there never lingers.
            folder = Path.Combine(Path.GetFullPath(root), "ue4ss-" + Guid.NewGuid().ToString("N"));
            using var http = Ue4ssOnline.CreateClient();
            var file = Ue4ssOnline.Run(() => Ue4ssDownloader.DownloadAsync(http, release.Urls, release.Sha256, folder));
            return new Ue4ssPackage(file, folder);
        }
        catch (ModException error)
        {
            // The downloader already deleted the file it refused; the folder around it goes too.
            TryDeleteFolder(folder);
            throw new CliError("download-failed", error.Message, details: ["Nothing was installed."]);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            TryDeleteFolder(folder);
            throw new CliError("download-failed",
                $"The release could not be downloaded into '{root}': {error.Message}", details: ["Nothing was installed."]);
        }
    }

    public void Dispose() => TryDeleteFolder(folder);

    private static void TryDeleteFolder(string folder)
    {
        try
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // The command still succeeded; only a temporary folder stays, and the app empties staging at its next start.
            Log.Warn($"The temporary folder '{folder}' could not be removed: {error.Message}");
        }
    }
}

/// <summary>
/// The online side of `ue4ss install`, `ue4ss update` and `ue4ss status --check`: the manifest that the game profile
/// names (or a test copy), read in order, then the release download. Every failure is a <see cref="CliError"/> with
/// text a player can act on.
/// </summary>
internal static class Ue4ssOnline
{
    /// <summary>As long as the app's own client; each manifest address keeps its own five seconds.</summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(3);

    /// <summary>
    /// The release to install: the first of the profile's manifest addresses that answers, else the profile's fixed
    /// package, as the app's one-click install reads them.
    /// </summary>
    /// <param name="manifestUrl">A test copy of the manifest, read instead of the profile's addresses.</param>
    /// <exception cref="CliError">No manifest answered, or one answered without an entry for this game and the profile names no fixed package.</exception>
    public static Ue4ssAvailableRelease FindRelease(CliContext context, string? manifestUrl)
    {
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
        if (context.Profile.Ue4ssPackageUrl is { Length: > 0 } fixedUrl)
        {
            var version = Ue4ssManifest.VersionFromFileName(fixedUrl) ?? "";
            return new Ue4ssAvailableRelease(
                new Ue4ssRelease(version, fixedUrl, context.Profile.Ue4ssPackageSha256), null);
        }

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

    /// <summary>The app's kind of client, so the manifest and GitHub see the same program.</summary>
    public static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = Timeout };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"UEModManager/{Program.ToolVersion}");
        return http;
    }

    /// <summary>
    /// Runs an async call on one of the thread pool's threads: a command may run while the app's window owns the
    /// current thread, and its waits must not need that thread to continue.
    /// </summary>
    public static T Run<T>(Func<Task<T>> work) => Task.Run(work).GetAwaiter().GetResult();
}
