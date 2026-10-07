using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using GameSync.Windows;

namespace GameSync.Host;

/// <summary>
/// BG-01, BG-09: GameSync's app runs once per Windows user and data folder. The running app holds a named mutex, and
/// listens on a pipe that only this Windows user can open; starting the app again sends it <c>show</c> over the pipe to
/// bring its window forward. The command line's jobs come the same way (<see cref="RunAsync"/>): a line
/// <c>run ["sync","--all"]</c>, answered by the job's lines, each after <c>| </c>, then <c>exit</c> and its code.
/// </summary>
public static class AppPipe
{
    private static readonly TimeSpan ReplyWithin = TimeSpan.FromSeconds(10);

    // A job can take minutes; a second start or a status meanwhile is served beside it.
    private const int AtOnce = 4;
    private const string JobLine = "| ";
    private const string Exit = "exit ";

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

    /// <summary>
    /// BG-09: runs a command-line job in the running app for this data folder, writing what the job prints to
    /// <paramref name="output"/> as it comes. Its exit code; null when no app answered, or one too old to run jobs, so the
    /// command runs here instead.
    /// </summary>
    public static async Task<int?> RunAsync(string dataDir, IReadOnlyList<string> args, TextWriter output, CancellationToken ct = default)
    {
        await using var pipe = new NamedPipeClientStream(".", NameFor(dataDir), PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try
        {
            await pipe.ConnectAsync(500, ct);
        }
        catch (Exception e) when (e is TimeoutException or IOException or UnauthorizedAccessException)
        {
            return null;
        }

        using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
        await using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
        var started = false;
        try
        {
            await writer.WriteLineAsync($"run {JsonSerializer.Serialize(args)}".AsMemory(), ct);
            while (await reader.ReadLineAsync(ct) is { } line)
            {
                if (line.StartsWith(JobLine, StringComparison.Ordinal))
                {
                    started = true;
                    await output.WriteLineAsync(line[JobLine.Length..]);
                }
                else if (line.StartsWith(Exit, StringComparison.Ordinal) && int.TryParse(line[Exit.Length..], out var code))
                {
                    return code;
                }
                else
                {
                    // An app from before jobs came this way answers "unknown"; nothing ran, so the command runs here.
                    return null;
                }
            }
        }
        catch (IOException) when (!started)
        {
            return null;
        }
        catch (IOException)
        {
        }

        await output.WriteLineAsync("GameSync's app stopped before the job finished. Run the command again.");
        return 1;
    }

    /// <summary>
    /// The running app's end: reads one line per connection and answers with <paramref name="handle"/>'s reply, until
    /// cancelled. A job's lines go to the client as the handler writes them to the writer it's given (BG-09). Up to four
    /// connections are served at once, so a long job doesn't hold a second start back.
    /// </summary>
    /// <param name="failed">Told once if the pipe can't be made, as when another program took its name; the app runs on without it.</param>
    public static async Task ServeAsync(string dataDir, Func<string, TextWriter, Task<string>> handle, Action<Exception> failed, CancellationToken ct)
    {
        var name = NameFor(dataDir);
        using var slots = new SemaphoreSlim(AtOnce);
        while (!ct.IsCancellationRequested)
        {
            NamedPipeServerStream pipe;
            try
            {
                await slots.WaitAsync(ct);
                pipe = new NamedPipeServerStream(name, PipeDirection.InOut, AtOnce, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                failed(e);
                return;
            }

            try
            {
                await pipe.WaitForConnectionAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                await pipe.DisposeAsync();
                return;
            }
            catch (IOException)
            {
                await pipe.DisposeAsync();
                slots.Release();
                continue;
            }

            // R11: nothing listens on the network: a client on another PC (the pipe reached as \\this-pc\pipe\…) is turned away.
            if (!IsLocal(pipe))
            {
                await pipe.DisposeAsync();
                slots.Release();
                continue;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    await AnswerAsync(pipe, handle, ct);
                }
                finally
                {
                    await pipe.DisposeAsync();
                    slots.Release();
                }
            }, CancellationToken.None);
        }
    }

    /// <summary>
    /// R11: whether the pipe's client is on this PC. Windows names a client's computer only when it came over the network,
    /// and says the pipe is local otherwise; anything else is taken as not local.
    /// </summary>
    internal static bool IsLocal(NamedPipeServerStream pipe)
    {
        var name = new StringBuilder(256);
        return !GetNamedPipeClientComputerName(pipe.SafePipeHandle, name, (uint)(name.Capacity * sizeof(char)))
            && Marshal.GetLastWin32Error() == PipeIsLocal;
    }

    private const int PipeIsLocal = 229;

    [DllImport("kernel32.dll", EntryPoint = "GetNamedPipeClientComputerNameW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetNamedPipeClientComputerName(Microsoft.Win32.SafeHandles.SafePipeHandle pipe, StringBuilder name, uint size);

    private static async Task AnswerAsync(NamedPipeServerStream pipe, Func<string, TextWriter, Task<string>> handle, CancellationToken ct)
    {
        try
        {
            using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
            await using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
            if (await reader.ReadLineAsync(ct).AsTask().WaitAsync(ReplyWithin, ct) is { } message)
            {
                // A job's lines, as it writes them, from whatever thread it writes on; the reply goes out even when the
                // message was "quit" and the app is already stopping.
                using var lines = TextWriter.Synchronized(new Prefixed(writer));
                var reply = await handle(message, lines);
                await writer.WriteLineAsync(reply.AsMemory(), CancellationToken.None);
            }
        }
        catch (Exception e) when (e is IOException or TimeoutException or OperationCanceledException or ObjectDisposedException)
        {
            // The other end went away, or the app is stopping; the next connection gets a fresh pipe.
        }
    }

    /// <summary>Each line a job writes, after <c>| </c>, so the command line tells it from the job's end.</summary>
    private sealed class Prefixed(TextWriter pipe) : TextWriter
    {
        private readonly StringBuilder _line = new();
        private bool _gone;

        public override Encoding Encoding => Encoding.UTF8;

        public override void Write(char value)
        {
            if (value == '\n')
            {
                Send(_line.ToString().TrimEnd('\r'));
                _line.Clear();
            }
            else
            {
                _line.Append(value);
            }
        }

        public override void Write(string? value)
        {
            foreach (var c in value ?? "")
            {
                Write(c);
            }
        }

        public override void WriteLine(string? value)
        {
            Write(value);
            Write('\n');
        }

        public override void WriteLine() => Write('\n');

        protected override void Dispose(bool disposing)
        {
            if (disposing && _line.Length > 0)
            {
                Send(_line.ToString());
                _line.Clear();
            }

            base.Dispose(disposing);
        }

        private void Send(string line)
        {
            if (_gone)
            {
                return;
            }

            try
            {
                pipe.WriteLine(JobLine + line);
            }
            catch (Exception e) when (e is IOException or ObjectDisposedException)
            {
                // The command line went away (its window closed, or Ctrl+C): the job finishes here all the same, so a
                // sync is never left half done, and what it says is in the activity log.
                _gone = true;
            }
        }
    }
}
