using System.Net.Sockets;
using System.Text;

namespace SmtpMqttGateway.Tests.TestSupport;

/// <summary>
/// A minimal, deliberately dumb SMTP client used only to drive protocol-level
/// integration tests (e.g. oversized message rejection) against a real
/// loopback listener. Not for production use.
/// </summary>
public sealed class RawSmtpClient : IAsyncDisposable
{
    private readonly TcpClient _client;
    private readonly NetworkStream _stream;
    private readonly StreamReader _reader;

    private RawSmtpClient(TcpClient client)
    {
        _client = client;
        _stream = client.GetStream();
        _reader = new StreamReader(_stream, Encoding.ASCII);
    }

    public static async Task<RawSmtpClient> ConnectAsync(string host, int port)
    {
        var client = new TcpClient();
        await client.ConnectAsync(host, port);
        var smtp = new RawSmtpClient(client);
        await smtp.ReadResponseAsync(); // greeting
        return smtp;
    }

    public async Task<string> SendAsync(string command)
    {
        var bytes = Encoding.ASCII.GetBytes(command + "\r\n");
        await _stream.WriteAsync(bytes);
        return await ReadResponseAsync();
    }

    public async Task<string> SendRawAsync(string raw)
    {
        var bytes = Encoding.ASCII.GetBytes(raw);
        await _stream.WriteAsync(bytes);
        return await ReadResponseAsync();
    }

    private async Task<string> ReadResponseAsync()
    {
        var lines = new List<string>();
        while (true)
        {
            var line = await _reader.ReadLineAsync() ?? throw new IOException("Connection closed before a full SMTP response was received.");
            lines.Add(line);

            // "250 " (space) marks the final line of a (possibly multi-line) reply; "250-" continues.
            if (line.Length < 4 || line[3] == ' ')
            {
                break;
            }
        }

        return string.Join('\n', lines);
    }

    public async ValueTask DisposeAsync()
    {
        _reader.Dispose();
        await _stream.DisposeAsync();
        _client.Dispose();
    }
}
