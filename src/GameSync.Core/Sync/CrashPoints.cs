using System.Runtime.CompilerServices;

namespace GameSync.Core.Sync;

public sealed class SimulatedCrashException(string point) : Exception($"Simulated crash at {point}.")
{
    public string Point { get; } = point;
}

/// <summary>
/// Named places where a crash would hurt most (BAK-08, BAK-12). Tests arm one to throw; setting the
/// GAMESYNC_CRASH_AT environment variable to a point's name kills the real process there.
/// </summary>
public static class CrashPoints
{
    public const string AfterBlobsBeforeRecord = "upload:after-blobs";
    public const string AfterStagingBeforeSwap = "restore:after-staging";
    public const string MidSwap = "restore:mid-swap";

    // A box, so disarming inside the async call that crashed is seen by the test that armed it; a plain
    // AsyncLocal value set inside an async method never flows back to its caller.
    private static readonly AsyncLocal<StrongBox<string?>?> Armed = new();

    public static void Arm(string? point) => Armed.Value = new StrongBox<string?>(point);

    public static void Hit(string point)
    {
        if (Armed.Value is { } box && box.Value == point)
        {
            box.Value = null;
            throw new SimulatedCrashException(point);
        }

        if (Environment.GetEnvironmentVariable("GAMESYNC_CRASH_AT") == point)
        {
            Environment.FailFast($"GAMESYNC_CRASH_AT={point}");
        }
    }
}
