using System.IO.Pipes;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using GameSync.Windows;

namespace GameSync.Host;

/// <summary>
/// BG-01, BG-09: GameSync's app runs once per Windows user and data folder. The running app holds a named mutex, and
/// listens on a pipe that only this Windows user can open; starting the app again sends it <c>show</c> over the pipe to
/// bring its window forward. The command line's jobs will come the same way.
/// </summary>
public static class AppPipe
{
    private static readonly TimeSpan ReplyWithin = TimeSpan.FromSeconds(10);

    /// <summary>The name the mutex and the pipe share: a hash of this Windows user and the data folder.</summary>
    public static string NameFor(string dataDir)
    {
        using var identity = WindowsIdentity.GetCurrent();
        var folder = Path.GetFullPath(dataDir).TrimEnd('\\').ToUpperInvariant();
        return "GameSync-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{identity.User?.Value}|{folder}")))[..20];
    }

    /// <summary>Claims "the app runs" for this user and data folder, held by the calling thread until disposed; null when another copy has it.</summary>
    public static Mutex? TryClaim(string dataDir)
    {
        var mutex = new Mutex(initiallyOwned: true, $@"Local\{NameFor(dataDir)}", out var createdNew);
        if (createdNew)
        {
            return mutex;
        }

        mutex.Dispose();
        return null;
    }

    /// <summary>
    /// Sends one line to the running app and returns its reply, or null when no app answered within
    /// <paramref name="connectWithin"/>. The app may bring its window forward, which Windows allows since this program
    /// was just started by the person.
    /// </summary>
    public static async Task<string?> SendAsync(string dataDir, string message, TimeSpan connectWithin, CancellationToken ct = default)
    {
        await using var pipe = new NamedPipeClientStream(".", NameFor(dataDir), PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try
        {
            await pipe.ConnectAsync((int)connectWithin.TotalMilliseconds, ct);
        }
        catch (Exception e) when (e is TimeoutException or IOException or UnauthorizedAccessException)
        {
            return null;
        }

        Foreground.LetPipeServerComeForward(pipe.SafePipeHandle);
        using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
        await using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
        try
        {
            await writer.WriteLineAsync(message.AsMemory(), ct);
            return await reader.ReadLineAsync(ct).AsTask().WaitAsync(ReplyWithin, ct);
        }
        catch (Exception e) when (e is TimeoutException or IOException)
        {
            return null;
        }
    }

    /// <summary>The running app's end: reads one line per connection and answers with <paramref name="handle"/>'s reply, until cancelled.</summary>
    /// <param name="failed">Told once if the pipe can't be made, as when another program took its name; the app runs on without it.</param>
    public static async Task ServeAsync(string dataDir, Func<string, Task<string>> handle, Action<Exception> failed, CancellationToken ct)
    {
        var name = NameFor(dataDir);
        while (!ct.IsCancellationRequested)
        {
            NamedPipeServerStream pipe;
            try
            {
                pipe = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                failed(e);
                return;
            }

            await using (pipe)
            {
                try
                {
                    await pipe.WaitForConnectionAsync(ct);
                    using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
                    await using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
                    if (await reader.ReadLineAsync(ct).AsTask().WaitAsync(ReplyWithin, ct) is { } message)
                    {
                        // The reply goes out even when the message was "quit" and the app is already stopping.
                        var reply = await handle(message);
                        await writer.WriteLineAsync(reply.AsMemory(), CancellationToken.None);
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception e) when (e is IOException or TimeoutException)
                {
                    // The other end went away; the next connection gets a fresh pipe.
                }
            }
        }
    }
}
