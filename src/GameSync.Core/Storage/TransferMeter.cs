using GameSync.Core.Model;

namespace GameSync.Core.Storage;

/// <summary>Which way a game's files are moving: up to the cloud, or down to this PC.</summary>
public enum TransferDirection
{
    Up,
    Down,
}

/// <summary>
/// How far an upload or a download of one game's files is (CLOUD-09, KAN-80): the files moved of how many (in an upload,
/// each version's record and each pin count as files too) and the save files' bytes before compression, the sizes
/// GameSync gives everywhere else.
/// </summary>
public sealed record TransferProgress(GameId Game, int FilesDone, int FilesTotal, long BytesDone, long BytesTotal,
    TransferDirection Direction = TransferDirection.Up)
{
    /// <summary>Everything counted has moved.</summary>
    public bool Finished => FilesDone >= FilesTotal && BytesDone >= BytesTotal;
}

/// <summary>
/// Counts one game's upload or download as it goes (KAN-80), with any number of files moving at once: each file's bytes
/// once it's there, and, while a cloud sends a big file in parts, the share of it sent so far (<see cref="Part"/>), so a
/// 300 MB world's bar moves as it goes instead of jumping when it's done. Reports keep their order: each one is made
/// under the meter's lock, so a report never goes back on an earlier one except when a part has to be sent again.
/// </summary>
public sealed class TransferMeter(GameId game, TransferDirection direction, IProgress<TransferProgress>? progress)
{
    private static readonly AsyncLocal<Action<double>?> CurrentPart = new();
    private readonly object _gate = new();
    private int _filesTotal;
    private long _bytesTotal;
    private int _files;
    private long _bytes;
    private long _partial;

    /// <summary>
    /// For a cloud that sends a file in parts: call with the share of the file under way that's sent so far, 0 to 1. Null
    /// when nothing counts the file this flow is moving.
    /// </summary>
    public static Action<double>? Part => CurrentPart.Value;

    /// <summary>Anything to move: a meter with nothing counted says nothing.</summary>
    public bool HasWork
    {
        get
        {
            lock (_gate)
            {
                return _filesTotal > 0 || _bytesTotal > 0;
            }
        }
    }

    /// <summary>More to move: files and their bytes (a version's record or a pin is a file counted with no bytes).</summary>
    public void Add(int files, long bytes)
    {
        lock (_gate)
        {
            _filesTotal += files;
            _bytesTotal += bytes;
        }
    }

    /// <summary>Says how much there is before the first file moves, so the bar is there from the start.</summary>
    public void Start()
    {
        lock (_gate)
        {
            Report();
        }
    }

    /// <summary>One file of no bytes has moved: a version's record, a pin, a pin taken away.</summary>
    public void Done()
    {
        lock (_gate)
        {
            _files++;
            Report();
        }
    }

    /// <summary>Moves one file of <paramref name="size"/> bytes with <paramref name="move"/>, counting its parts as they go and all of it once it's there.</summary>
    public async Task MoveAsync(long size, Func<Task> move)
    {
        long sent = 0;

        // Set inside this method, so it's this file's alone: whatever runs beside it on other flows has its own.
        CurrentPart.Value = share =>
        {
            var now = (long)(Math.Clamp(share, 0, 1) * size);
            lock (_gate)
            {
                _partial += now - sent;
                sent = now;
                Report();
            }
        };
        var moved = false;
        try
        {
            await move();
            moved = true;
        }
        finally
        {
            CurrentPart.Value = null;
            lock (_gate)
            {
                _partial -= sent;
                if (moved)
                {
                    _files++;
                    _bytes += size;
                }

                Report();
            }
        }
    }

    private void Report()
    {
        if (_filesTotal > 0 || _bytesTotal > 0)
        {
            progress?.Report(new TransferProgress(game, _files, _filesTotal, Math.Min(_bytes + _partial, _bytesTotal), _bytesTotal, direction));
        }
    }
}
