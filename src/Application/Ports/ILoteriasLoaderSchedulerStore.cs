using Lotofacil.Loader.Domain;

namespace Lotofacil.Loader.Application;

public interface ILoteriasLoaderSchedulerStore
{
    Task<LoteriasLoaderSchedulerState?> TryReadAsync(CancellationToken ct);

    Task WriteAsync(LoteriasLoaderSchedulerState state, CancellationToken ct);
}
