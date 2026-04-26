namespace Lotofacil.Loader.Application;

public interface ILotofacilStateStore
{
    Task<object?> TryReadRawAsync(CancellationToken ct);
    Task WriteRawAsync(object state, CancellationToken ct);
}

