using Azure;
using Azure.Data.Tables;
using Lotofacil.Loader.Application;
using Lotofacil.Loader.Domain;
using Microsoft.Extensions.Options;

namespace Lotofacil.Loader.Infrastructure;

public sealed class AzureTableLoteriaStateStore : ILoteriaStateStore
{
    private const string RowKeyValue = "Loader";

    private readonly TableClient _table;
    private readonly string _partitionKey;

    public AzureTableLoteriaStateStore(IOptions<StorageOptions> storage, string partitionKey)
    {
        var opt = storage.Value;
        _table = new TableClient(opt.ConnectionString, opt.LoteriasStateTable);
        _partitionKey = partitionKey;
    }

    /// <summary>Para testes e composição manual sem IOptions.</summary>
    public AzureTableLoteriaStateStore(string connectionString, string tableName, string partitionKey)
    {
        _table = new TableClient(connectionString, tableName);
        _partitionKey = partitionKey;
    }

    public async Task<object?> TryReadRawAsync(CancellationToken ct)
    {
        await _table.CreateIfNotExistsAsync(ct);

        try
        {
            var resp = await _table.GetEntityAsync<TableEntity>(_partitionKey, RowKeyValue, cancellationToken: ct);
            var e = resp.Value;

            var lastId = e.GetInt32("LastLoadedContestId") ?? 0;
            var lastDate = e.TryGetValue("LastLoadedDrawDate", out var v) ? v as string : null;

            return new LoteriaLoaderState(
                LastLoadedContestId: lastId,
                LastLoadedDrawDate: lastDate,
                LastUpdatedAtUtc: e.GetDateTimeOffset("LastUpdatedAtUtc") ?? DateTimeOffset.MinValue,
                ETag: e.ETag.ToString()
            );
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task WriteRawAsync(object state, CancellationToken ct)
    {
        await _table.CreateIfNotExistsAsync(ct);

        if (state is not LoteriaLoaderState st)
        {
            throw new InvalidOperationException($"Expected {nameof(LoteriaLoaderState)}.");
        }

        var entity = new TableEntity(_partitionKey, RowKeyValue)
        {
            ["LastLoadedContestId"] = st.LastLoadedContestId,
            ["LastLoadedDrawDate"] = st.LastLoadedDrawDate,
            ["LastUpdatedAtUtc"] = st.LastUpdatedAtUtc
        };

        if (string.IsNullOrWhiteSpace(st.ETag))
        {
            await _table.UpsertEntityAsync(entity, TableUpdateMode.Replace, ct);
            return;
        }

        await _table.UpdateEntityAsync(entity, new ETag(st.ETag), TableUpdateMode.Replace, ct);
    }
}
