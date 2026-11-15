using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace UEManager.Core.Tests;

/// <summary>The program's web client asks for compressed answers and hands on the unpacked text.</summary>
public class HttpClientsTests
{
    [Theory]
    [InlineData("gzip")]
    [InlineData("br")]
    [InlineData(null)]
    public async Task A_compressed_answer_arrives_unpacked_and_the_client_asks_for_one(string? encoding)
    {
        var text = "{\"openmods\": 1, \"mods\": [" + string.Join(",", Enumerable.Repeat("{\"name\": \"A mod\"}", 200)) + "]}";
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var serving = ServeOnceAsync(listener, Encoding.UTF8.GetBytes(text), encoding);
            using var http = HttpClients.Create("UEModManager/test", TimeSpan.FromSeconds(10));

            var answer = await http.GetStringAsync($"http://127.0.0.1:{port}/games/mods.json");
            var request = await serving;

            Assert.Equal(text, answer);
            Assert.Contains("UEModManager/test", request);
            var accepted = request.Split("\r\n").Single(line => line.StartsWith("Accept-Encoding:", StringComparison.OrdinalIgnoreCase));
            Assert.Contains("gzip", accepted);
            Assert.Contains("br", accepted);
        }
        finally
        {
            listener.Stop();
        }
    }

    /// <summary>Answers one request with the body, compressed as named (null: as it is); returns the request's head.</summary>
    private static async Task<string> ServeOnceAsync(TcpListener listener, byte[] body, string? encoding)
    {
        using var client = await listener.AcceptTcpClientAsync();
        var stream = client.GetStream();
        var head = new StringBuilder();
        var buffer = new byte[4096];
        while (!head.ToString().Contains("\r\n\r\n"))
        {
            var read = await stream.ReadAsync(buffer);
            if (read == 0)
                break;
            head.Append(Encoding.ASCII.GetString(buffer, 0, read));
        }
        var sent = encoding switch
        {
            "gzip" => Pack(body, output => new GZipStream(output, CompressionLevel.Optimal)),
            "br" => Pack(body, output => new BrotliStream(output, CompressionLevel.Optimal)),
            _ => body,
        };
        var header = "HTTP/1.1 200 OK\r\nContent-Type: application/json\r\n"
            + (encoding is null ? "" : $"Content-Encoding: {encoding}\r\n")
            + $"Content-Length: {sent.Length}\r\nConnection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(header));
        await stream.WriteAsync(sent);
        await stream.FlushAsync();
        return head.ToString();
    }

    private static byte[] Pack(byte[] body, Func<Stream, Stream> compressor)
    {
        using var output = new MemoryStream();
        using (var packer = compressor(output))
            packer.Write(body);
        return output.ToArray();
    }
}
