using System.Diagnostics;

namespace Lotofacil.Loader.Application;

public sealed class AsyncLocalRunContext : IRunContext
{
    private static readonly AsyncLocal<State?> Local = new();

    public RunContextSnapshot? Current
        => Local.Value is not { } s
            ? null
            : new RunContextSnapshot(
                RunId: s.RunId,
                Modality: s.Modality,
                RetriesCount: s.RetriesCount,
                RateLimitWaitSecondsTotal: s.RateLimitWaitSecondsTotal);

    public IDisposable BeginRun(string runId, string modality)
    {
        var prior = Local.Value;
        Local.Value = new State(runId, modality);
        return new Pop(prior);
    }

    public void IncrementRetries(int count = 1)
    {
        if (Local.Value is { } s)
        {
            s.RetriesCount += count;
        }
    }

    public void AddWaitSeconds(double seconds)
    {
        if (Local.Value is { } s)
        {
            s.RateLimitWaitSecondsTotal += seconds;
        }
    }

    private sealed class Pop : IDisposable
    {
        private readonly State? _prior;
        private bool _disposed;

        public Pop(State? prior) => _prior = prior;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            Local.Value = _prior;
            _disposed = true;
        }
    }

    private sealed class State
    {
        public State(string runId, string modality)
        {
            RunId = runId;
            Modality = modality;
        }

        public string RunId { get; }
        public string Modality { get; }
        public int RetriesCount { get; set; }
        public double RateLimitWaitSecondsTotal { get; set; }
    }
}

