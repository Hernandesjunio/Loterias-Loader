namespace Lotofacil.Loader.Application;

public interface ILotofacilBlobStore
{
    Task<object?> TryReadRawAsync(CancellationToken ct);
    Task WriteRawAsync(object document, CancellationToken ct);
}

