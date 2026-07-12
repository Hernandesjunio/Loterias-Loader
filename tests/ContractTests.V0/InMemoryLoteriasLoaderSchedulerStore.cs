using Lotofacil.Loader.Application;
using Lotofacil.Loader.Domain;

namespace ContractTests.V0;

internal sealed class InMemoryLoteriasLoaderSchedulerStore : ILoteriasLoaderSchedulerStore
{
    private LoteriasLoaderSchedulerState? _state;
    private int _writeCount;

    public bool SimulateEtagConflictOnNextWrite { get; set; }

    public int WriteCount => _writeCount;

    public LoteriasLoaderSchedulerState? CurrentState => _state;

    public IReadOnlyList<string> AcquiredModalities { get; } = new List<string>();

    public void Seed(LoteriasLoaderSchedulerState state) => _state = state;

    public Task<LoteriasLoaderSchedulerState?> TryReadAsync(CancellationToken ct) =>
        Task.FromResult(_state);

    public Task WriteAsync(LoteriasLoaderSchedulerState state, CancellationToken ct)
    {
        if (SimulateEtagConflictOnNextWrite)
        {
            SimulateEtagConflictOnNextWrite = false;
            throw new SchedulerConcurrencyException("Simulated scheduler ETag conflict.");
        }

        if (!string.IsNullOrWhiteSpace(state.ETag)
            && _state?.ETag is not null
            && !string.Equals(state.ETag, _state.ETag, StringComparison.Ordinal))
        {
            throw new SchedulerConcurrencyException("Scheduler state changed concurrently.");
        }

        _writeCount++;
        _state = state with { ETag = $"etag-{_writeCount}" };
        return Task.CompletedTask;
    }
}
