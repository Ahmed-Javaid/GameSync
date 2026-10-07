namespace GameSync.Host;

/// <summary>
/// BG-09: one engine touches saves at a time, across processes; the command line and the agent each hold this while
/// they work. It's a file opened for this process alone, so Windows lets go of it if the process dies, and unlike a
/// mutex it doesn't belong to a thread, which async work would break.
/// </summary>
internal sealed class EngineLock : IDisposable
{
    private const int SharingViolation = unchecked((int)0x80070020);
    private const int LockViolation = unchecked((int)0x80070021);

    private readonly FileStream _file;

    private EngineLock(FileStream file) => _file = file;

    public void Dispose() => _file.Dispose();

    /// <summary>Waits for the lock, telling <paramref name="waiting"/> once if it has to.</summary>
    public static async Task<EngineLock> AcquireAsync(string dataDir, Action? waiting, CancellationToken ct)
    {
        var path = Path.Combine(dataDir, "engine.lock");
        var told = false;
        while (true)
        {
            if (TryOpen(path) is { } file)
            {
                return new EngineLock(file);
            }

            if (!told)
            {
                waiting?.Invoke();
                told = true;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250), ct);
        }
    }

    /// <summary>
    /// KAN-88: one upload at a time, in any process: the app's uploads beside its rounds, and a command line's or the
    /// daily task's within their runs. It never waits for the engine's lock while held, so the two can't deadlock.
    /// </summary>
    public static async Task<EngineLock> AcquireUploadAsync(string dataDir, Action? waiting, CancellationToken ct)
    {
        var path = Path.Combine(dataDir, "upload.lock");
        var told = false;
        while (true)
        {
            if (TryOpen(path) is { } file)
            {
                return new EngineLock(file);
            }

            if (!told)
            {
                waiting?.Invoke();
                told = true;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250), ct);
        }
    }

    /// <summary>BG-01: the agent's own lock, held as long as it runs; null when another agent already has it.</summary>
    public static EngineLock? TryAcquireAgent(string dataDir) => TryOpen(Path.Combine(dataDir, "agent.lock")) is { } file ? new EngineLock(file) : null;

    public static bool AgentRunning(string dataDir)
    {
        using var probe = TryAcquireAgent(dataDir);
        return probe is null;
    }

    private static FileStream? TryOpen(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException e) when (e.HResult is SharingViolation or LockViolation)
        {
            return null;
        }
    }
}
