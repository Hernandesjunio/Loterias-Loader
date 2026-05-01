namespace Lotofacil.Loader.Application;

public interface ILoteriaStateStore
{
    Task<object?> TryReadRawAsync(CancellationToken ct);
    Task WriteRawAsync(object state, CancellationToken ct);
}
