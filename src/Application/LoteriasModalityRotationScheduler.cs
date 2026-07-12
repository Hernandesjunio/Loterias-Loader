using Lotofacil.Loader.Domain;

namespace Lotofacil.Loader.Application;

public sealed class LoteriasModalityRotationScheduler : ILoteriasLoaderScheduler
{
    private readonly ILoteriasLoaderSchedulerStore _store;
    private readonly IClock _clock;
    private readonly IReadOnlyList<string> _modalityOrder;

    public LoteriasModalityRotationScheduler(
        ILoteriasLoaderSchedulerStore store,
        IClock clock,
        IReadOnlyList<string> modalityOrder)
    {
        _store = store;
        _clock = clock;
        _modalityOrder = modalityOrder;
    }

    public async Task<LoteriasSchedulerAcquireResult> AcquireNextModalityAsync(CancellationToken ct)
    {
        EnsureOrderNotEmpty();

        var state = await _store.TryReadAsync(ct)
            ?? new LoteriasLoaderSchedulerState(0, null, null, null);

        var index = NormalizeIndex(state.NextModalityIndex);
        var modalityKey = _modalityOrder[index];

        return new LoteriasSchedulerAcquireResult(modalityKey, index, state);
    }

    public async Task AdvanceAfterAttemptAsync(LoteriasSchedulerAcquireResult acquire, CancellationToken ct)
    {
        EnsureOrderNotEmpty();

        var nextIndex = (acquire.Index + 1) % _modalityOrder.Count;
        var updated = new LoteriasLoaderSchedulerState(
            NextModalityIndex: nextIndex,
            LastModalityKey: acquire.ModalityKey,
            LastRunUtc: _clock.UtcNow,
            ETag: acquire.State.ETag);

        await _store.WriteAsync(updated, ct);
    }

    private int NormalizeIndex(int index)
    {
        var count = _modalityOrder.Count;
        var normalized = index % count;
        if (normalized < 0)
        {
            normalized += count;
        }

        return normalized;
    }

    private void EnsureOrderNotEmpty()
    {
        if (_modalityOrder.Count == 0)
        {
            throw new InvalidOperationException("LoteriasLoader modality order must not be empty.");
        }
    }
}
