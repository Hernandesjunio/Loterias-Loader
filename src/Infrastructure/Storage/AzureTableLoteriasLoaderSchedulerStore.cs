using Azure;
using Azure.Data.Tables;
using Lotofacil.Loader.Application;
using Lotofacil.Loader.Domain;
using Microsoft.Extensions.Options;

namespace Lotofacil.Loader.Infrastructure;

public sealed class AzureTableLoteriasLoaderSchedulerStore : ILoteriasLoaderSchedulerStore
{
    private readonly TableClient _table;

    public AzureTableLoteriasLoaderSchedulerStore(IOptions<StorageOptions> storage)
    {
        var opt = storage.Value;
        _table = new TableClient(opt.ConnectionString, opt.LoteriasStateTable);
    }

    /// <summary>Para testes e composição manual sem IOptions.</summary>
    public AzureTableLoteriasLoaderSchedulerStore(string connectionString, string tableName)
    {
        _table = new TableClient(connectionString, tableName);
    }

    public async Task<LoteriasLoaderSchedulerState?> TryReadAsync(CancellationToken ct)
    {
        await _table.CreateIfNotExistsAsync(ct);

        try
        {
            var resp = await _table.GetEntityAsync<TableEntity>(
                LoteriasLoaderSchedulerTableKeys.PartitionKey,
                LoteriasLoaderSchedulerTableKeys.RowKey,
                cancellationToken: ct);

            return MapEntity(resp.Value);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task WriteAsync(LoteriasLoaderSchedulerState state, CancellationToken ct)
    {
        await _table.CreateIfNotExistsAsync(ct);

        var entity = new TableEntity(
            LoteriasLoaderSchedulerTableKeys.PartitionKey,
            LoteriasLoaderSchedulerTableKeys.RowKey)
        {
            ["NextModalityIndex"] = state.NextModalityIndex,
            ["LastModalityKey"] = state.LastModalityKey,
            ["LastRunUtc"] = state.LastRunUtc
        };

        if (string.IsNullOrWhiteSpace(state.ETag))
        {
            await _table.UpsertEntityAsync(entity, TableUpdateMode.Replace, ct);
            return;
        }

        try
        {
            await _table.UpdateEntityAsync(entity, new ETag(state.ETag), TableUpdateMode.Replace, ct);
        }
        catch (RequestFailedException ex) when (ex.Status == 412)
        {
            throw new SchedulerConcurrencyException("Scheduler state changed concurrently.", ex);
        }
    }

    private static LoteriasLoaderSchedulerState MapEntity(TableEntity entity)
    {
        var index = entity.GetInt32("NextModalityIndex") ?? 0;
        var lastModality = entity.TryGetValue("LastModalityKey", out var modalityValue)
            ? modalityValue as string
            : null;
        var lastRunUtc = entity.GetDateTimeOffset("LastRunUtc");

        return new LoteriasLoaderSchedulerState(
            NextModalityIndex: index,
            LastModalityKey: lastModality,
            LastRunUtc: lastRunUtc,
            ETag: entity.ETag.ToString());
    }
}
