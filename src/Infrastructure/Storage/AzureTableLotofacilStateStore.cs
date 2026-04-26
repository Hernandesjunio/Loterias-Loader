using Azure;
using Azure.Data.Tables;
using Lotofacil.Loader.Application;
using Lotofacil.Loader.Domain;
using Microsoft.Extensions.Options;

namespace Lotofacil.Loader.Infrastructure;

public sealed class AzureTableLotofacilStateStore : ILotofacilStateStore
{
    private const string PartitionKeyValue = "Lotofacil";
    private const string RowKeyValue = "Loader";

    private readonly TableClient _table;

    public AzureTableLotofacilStateStore(IOptions<StorageOptions> storage)
    {
        var opt = storage.Value;
        _table = new TableClient(opt.ConnectionString, opt.LotofacilStateTable);
    }

    public async Task<object?> TryReadRawAsync(CancellationToken ct)
    {
        await _table.CreateIfNotExistsAsync(ct);

        try
        {
            var resp = await _table.GetEntityAsync<TableEntity>(PartitionKeyValue, RowKeyValue, cancellationToken: ct);
            var e = resp.Value;

            var lastId = e.GetInt32("LastLoadedContestId") ?? 0;
            var lastDate = e.TryGetValue("LastLoadedDrawDate", out var v) ? v as string : null;

            return new LotofacilState(
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

        if (state is not LotofacilState st)
        {
            throw new InvalidOperationException($"Expected {nameof(LotofacilState)}.");
        }

        var entity = new TableEntity(PartitionKeyValue, RowKeyValue)
        {
            ["LastLoadedContestId"] = st.LastLoadedContestId,
            ["LastLoadedDrawDate"] = st.LastLoadedDrawDate,
            ["LastUpdatedAtUtc"] = st.LastUpdatedAtUtc
        };

        if (string.IsNullOrWhiteSpace(st.ETag))
        {
            // Primeira escrita (sem ETag): upsert é suficiente.
            await _table.UpsertEntityAsync(entity, TableUpdateMode.Replace, ct);
            return;
        }

        // Concurrency: ETag obrigatório (seção 8/13). Em conflito, o caso de uso deve parar com segurança.
        await _table.UpdateEntityAsync(entity, new ETag(st.ETag), TableUpdateMode.Replace, ct);
    }
}

