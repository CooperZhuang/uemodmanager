namespace UEManager.Core.Updates;

/// <summary>Where the app's remote data, the UE4SS manifest, came from at this start.</summary>
public enum RemoteSource
{
    /// <summary>Not read yet: it is read once at every start.</summary>
    Checking,

    /// <summary>Cloudflare Pages, which mirrors the metadata repo: under the app's own domain or its pages.dev name.</summary>
    Cloudflare,

    /// <summary>GitHub itself, the backup.</summary>
    GitHub,

    /// <summary>Another address, such as a test copy given on the command line.</summary>
    Other,

    /// <summary>No address answered.</summary>
    Offline,
}

public static class RemoteSources
{
    /// <summary>The host behind an address of the remote data.</summary>
    public static RemoteSource Of(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return RemoteSource.Other;
        if (IsUnder(uri.Host, "github.com") || IsUnder(uri.Host, "githubusercontent.com"))
            return RemoteSource.GitHub;
        if (IsUnder(uri.Host, "pages.dev") || IsUnder(uri.Host, "openmods.nexus"))
            return RemoteSource.Cloudflare;
        return RemoteSource.Other;
    }

    private static bool IsUnder(string host, string domain) =>
        host.Equals(domain, StringComparison.OrdinalIgnoreCase) || host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase);
}
