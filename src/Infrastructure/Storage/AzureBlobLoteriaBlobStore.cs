using System.Text;
using System.Text.Json;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Lotofacil.Loader.Application;

namespace Lotofacil.Loader.Infrastructure;

public sealed class AzureBlobLoteriaBlobStore : ILoteriaBlobStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly BlobContainerClient _container;
    private readonly string _blobName;

    public AzureBlobLoteriaBlobStore(string connectionString, string containerName, string blobName)
    {
        _container = new BlobContainerClient(connectionString, containerName);
        _blobName = blobName;
    }

    public async Task<object?> TryReadRawAsync(CancellationToken ct)
    {
        await _container.CreateIfNotExistsAsync(cancellationToken: ct);
        var blob = _container.GetBlobClient(_blobName);

        if (!await blob.ExistsAsync(ct))
        {
            return null;
        }

        var dl = await blob.DownloadContentAsync(ct);
        var json = dl.Value.Content.ToString();
        return JsonDocument.Parse(json);
    }

    public async Task WriteRawAsync(object document, CancellationToken ct)
    {
        await _container.CreateIfNotExistsAsync(cancellationToken: ct);
        var blob = _container.GetBlobClient(_blobName);

        var json = document is string s ? s : JsonSerializer.Serialize(document, JsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);

        using var ms = new MemoryStream(bytes);
        await blob.UploadAsync(ms, overwrite: true, cancellationToken: ct);
        await blob.SetHttpHeadersAsync(
            httpHeaders: new BlobHttpHeaders { ContentType = "application/json; charset=utf-8" },
            cancellationToken: ct
        );
    }
}
