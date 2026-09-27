using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Web;

namespace GameSync.Storage.Drive;

/// <summary>
/// R11: the one thing GameSync listens on, only while a sign-in waits for Google's redirect: 127.0.0.1 on a port
/// Windows picks, closed as soon as the redirect arrives or the wait times out.
/// </summary>
internal sealed class LoopbackRedirect : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);

    public LoopbackRedirect()
    {
        _listener.Start();
        Endpoint = (IPEndPoint)_listener.LocalEndpoint;
    }

    public IPEndPoint Endpoint { get; }

    public string Uri => $"http://127.0.0.1:{Endpoint.Port}/";

    /// <summary>The redirect's query parameters. Other requests, such as the browser asking for a favicon, get a 404.</summary>
    public async Task<IReadOnlyDictionary<string, string>> WaitAsync(TimeSpan timeout, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try
        {
            while (true)
            {
                using var client = await _listener.AcceptTcpClientAsync(cts.Token);
                await using var stream = client.GetStream();
                var requestLine = await ReadLineAsync(stream, cts.Token);
                while ((await ReadLineAsync(stream, cts.Token)).Length > 0)
                {
                    // Headers aren't needed.
                }

                var target = requestLine.Split(' ') is [_, var t, ..] ? t : "";
                var queryAt = target.IndexOf('?', StringComparison.Ordinal);
                var path = queryAt < 0 ? target : target[..queryAt];
                if (path != "/" || queryAt < 0)
                {
                    await RespondAsync(stream, "404 Not Found", "Not found.", cts.Token);
                    continue;
                }

                var parsed = HttpUtility.ParseQueryString(target[(queryAt + 1)..]);
                var query = parsed.AllKeys.OfType<string>().ToDictionary(k => k, k => parsed[k] ?? "", StringComparer.Ordinal);
                var page = query.ContainsKey("error")
                    ? "GameSync wasn't signed in. You can close this tab and try again."
                    : "GameSync is signed in to Google Drive. You can close this tab.";
                await RespondAsync(stream, "200 OK", page, cts.Token);
                return query;
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException("Nobody finished signing in within the time allowed. Run 'gamesync signin' to try again.");
        }
        finally
        {
            _listener.Stop();
        }
    }

    public void Dispose() => _listener.Stop();

    private static async Task<string> ReadLineAsync(NetworkStream stream, CancellationToken ct)
    {
        var line = new StringBuilder();
        var one = new byte[1];
        while (line.Length < 8192 && await stream.ReadAsync(one, ct) == 1)
        {
            if (one[0] == '\n')
            {
                break;
            }

            if (one[0] != '\r')
            {
                line.Append((char)one[0]);
            }
        }

        return line.ToString();
    }

    private static async Task RespondAsync(NetworkStream stream, string status, string text, CancellationToken ct)
    {
        var body = Encoding.UTF8.GetBytes($"<!doctype html><meta charset=\"utf-8\"><title>GameSync</title><p style=\"font:16px system-ui;margin:3em\">{WebUtility.HtmlEncode(text)}</p>");
        var head = Encoding.ASCII.GetBytes($"HTTP/1.1 {status}\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(head, ct);
        await stream.WriteAsync(body, ct);
    }
}
