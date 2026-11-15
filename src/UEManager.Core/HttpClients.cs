using System.Net;

namespace UEManager.Core;

/// <summary>The web client of the app and of its command line: what every request of the program needs, in one place.</summary>
public static class HttpClients
{
    /// <summary>
    /// A client that names the program and asks for compressed answers (gzip, Brotli, deflate), as browsers do (the
    /// user's choice, 2026-11-16): text such as the openmods lists then arrives several times smaller (Phantom Blade Zero's
    /// list: 17 KB instead of 101 KB). Zips and pictures come as they are: servers do not compress them. Every read sees
    /// the unpacked bytes, so size limits and SHA-256 checks work as before. Only a compressed answer's size header is
    /// gone, and nothing relies on it: the limits count the bytes read, and a download's progress falls back to the
    /// size the release names.
    /// </summary>
    /// <param name="userAgent">Such as "UEModManager/2.3.1".</param>
    public static HttpClient Create(string userAgent, TimeSpan timeout)
    {
        var http = new HttpClient(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All }) { Timeout = timeout };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
        return http;
    }
}
