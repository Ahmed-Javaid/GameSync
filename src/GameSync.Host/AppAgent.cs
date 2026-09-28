namespace GameSync.Host;

/// <summary>
/// The agent inside GameSync's app (design.md → Background work). It starts once GameSync is set up on this PC and no
/// other agent holds the data folder, such as <c>gamesync agent</c> in a terminal, and checks again every minute until
/// it can. It stops when the app quits.
/// </summary>
public sealed class AppAgent(string dataDir, IAgentOutput output)
{
    private static readonly TimeSpan TryAgainEvery = TimeSpan.FromMinutes(1);

    private readonly SemaphoreSlim _kick = new(0);
    private volatile Agent? _agent;

    /// <summary>The agent watches and syncs from this app; false until GameSync is set up, and while another agent runs.</summary>
    public bool IsRunning => _agent is not null;

    public async Task RunAsync(CancellationToken ct)
    {
        var told = false;
        while (!ct.IsCancellationRequested)
        {
            if (File.Exists(AppConfig.PathIn(dataDir)))
            {
                using var agentLock = EngineLock.TryAcquireAgent(dataDir);
                if (agentLock is not null)
                {
                    using var agent = new Agent(dataDir, output);
                    _agent = agent;
                    try
                    {
                        await agent.RunAsync(ct);
                    }
                    finally
                    {
                        _agent = null;
                    }

                    return;
                }

                if (!told)
                {
                    output.Say("Another GameSync agent is running on this PC, such as 'gamesync agent' in a terminal. This app takes over once it stops.");
                    told = true;
                }
            }

            try
            {
                await _kick.WaitAsync(TryAgainEvery, ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>Sync now: every game syncs at the agent's next round with nothing playing.</summary>
    public void SyncNow() => _agent?.SyncSoon();

    /// <summary>GameSync was just set up here: the agent starts now rather than within the minute.</summary>
    public void Kick() => _kick.Release();
}
