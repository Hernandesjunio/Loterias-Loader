namespace Lotofacil.Loader.Application;

public interface ILoteriaBlobStore
{
    Task<object?> TryReadRawAsync(CancellationToken ct);
    Task WriteRawAsync(object document, CancellationToken ct);
}
