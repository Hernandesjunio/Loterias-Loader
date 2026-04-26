namespace Lotofacil.Loader.Infrastructure;

public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    public required string ConnectionString { get; init; }
    public required string BlobContainer { get; init; }
    public required string LotofacilBlobName { get; init; }
    public required string LotofacilStateTable { get; init; }
}

