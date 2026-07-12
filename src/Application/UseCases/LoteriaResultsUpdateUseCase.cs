using System.Diagnostics;
using System.Text.Json;
using Lotofacil.Loader.Domain;
using Microsoft.Extensions.Logging;

namespace Lotofacil.Loader.Application;

public sealed class LoteriaResultsUpdateUseCase
{
    private readonly ILogger<LoteriaResultsUpdateUseCase> _log;
    private readonly IRunContext _runContext;
    private readonly IClock _clock;
    private readonly ILotteriesApiClient _api;
    private readonly ILoteriaBlobStore _blob;
    private readonly ILoteriaStateStore _state;
    private readonly ILoteriaBlobCatalog _catalog;
    private readonly string _modalityKey;
    private readonly string _lotteryApiSegment;

    public LoteriaResultsUpdateUseCase(
        ILogger<LoteriaResultsUpdateUseCase> log,
        IRunContext runContext,
        IClock clock,
        ILotteriesApiClient api,
        ILoteriaBlobStore blob,
        ILoteriaStateStore state,
        ILoteriaBlobCatalog catalog,
        string modalityKey,
        string lotteryApiSegment)
    {
        _log = log;
        _runContext = runContext;
        _clock = clock;
        _api = api;
        _blob = blob;
        _state = state;
        _catalog = catalog;
        _modalityKey = modalityKey;
        _lotteryApiSegment = lotteryApiSegment;
    }

    public string ModalityKey => _modalityKey;

    public async Task<UpdateLoteriaResultsOutcome> ExecuteAsync(CancellationToken ct)
    {
        var startUtc = _clock.UtcNow;
        var startTimestamp = Stopwatch.GetTimestamp();
        var nowUtc = _clock.UtcNow;
        const int deadlineSeconds = 180;
        var deadlineUtc = nowUtc.AddSeconds(deadlineSeconds);
        var budget = new ExecutionBudget(_clock, deadlineUtc);
        _runContext.SetExecutionBudget(budget);

        using var budgetCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budgetCts.CancelAfter(deadlineUtc - _clock.UtcNow);
        var budgetCt = budgetCts.Token;

        try
        {
            return await ExecuteCoreAsync(
                startUtc,
                startTimestamp,
                deadlineSeconds,
                budget,
                budgetCt,
                ct);
        }
        finally
        {
            _runContext.SetExecutionBudget(null);
        }
    }

    private async Task<UpdateLoteriaResultsOutcome> ExecuteCoreAsync(
        DateTimeOffset startUtc,
        long startTimestamp,
        int deadlineSeconds,
        IExecutionBudget budget,
        CancellationToken budgetCt,
        CancellationToken hostCt)
    {
        var ctx = _runContext.Current;
        using var activity = StartRootActivity(ctx, deadlineSeconds);
        using var scope = _log.BeginScope(new Dictionary<string, object?>
        {
            ["run_id"] = ctx?.RunId,
            ["modality"] = _modalityKey,
            ["trace_id"] = Activity.Current?.TraceId.ToString(),
            ["deadline_seconds"] = deadlineSeconds,
            ["timezone"] = "America/Sao_Paulo"
        });

        _log.LogDebug("update_results.start now_utc={now_utc}", _clock.UtcNow);

        _log.LogDebug("state.read.start");
        EmitEvent("state.read.start");
        var state = await ReadOrInitializeStateAsync(budget, budgetCt);
        EmitEvent("state.read.ok");
        _log.LogDebug(
            "state.read.ok last_loaded_contest_id={last_loaded_contest_id} last_loaded_draw_date={last_loaded_draw_date}",
            state.LastLoadedContestId,
            state.LastLoadedDrawDate);

        if (!HasMinimumBudget(budget))
        {
            _log.LogDebug("budget.insufficient reason_stop={reason_stop}", ReasonStop.SAFE_STOP_WINDOW_EXPIRED);
            return FinalizeAndReturn(Outcome(
                ReasonStop.SAFE_STOP_WINDOW_EXPIRED,
                state.LastLoadedContestId,
                null,
                0,
                state.LastLoadedContestId,
                deadlineSeconds), startUtc, startTimestamp);
        }

        _log.LogDebug("blob.read.start");
        EmitEvent("blob.read.start");
        var existingDoc = await ReadBlobDocumentAsync(budgetCt);
        EmitEvent("blob.read.ok");
        var existingDraws = ToDrawMap(existingDoc);
        if (existingDraws.Count > 0 && state.LastLoadedContestId > existingDraws.Keys.Max())
        {
            var blobMax = existingDraws.Keys.Max();
            _log.LogError(
                "state.inconsistent reason_stop={reason_stop} table_last_loaded_contest_id={table_last_loaded_contest_id} blob_max_contest_id={blob_max_contest_id}",
                ReasonStop.HARD_FAIL_STATE_INCONSISTENT_TABLE_GT_BLOB,
                state.LastLoadedContestId,
                blobMax);
            return FinalizeAndReturn(Outcome(
                ReasonStop.HARD_FAIL_STATE_INCONSISTENT_TABLE_GT_BLOB,
                state.LastLoadedContestId,
                null,
                0,
                state.LastLoadedContestId,
                deadlineSeconds), startUtc, startTimestamp);
        }

        _log.LogDebug("sync.all.start");
        EmitEvent("sync.all.start");
        object rawAll;
        try
        {
            rawAll = await _api.GetAllResultsRawAsync(_lotteryApiSegment, budgetCt);
        }
        catch (BudgetExceededException)
        {
            _log.LogWarning("sync.all.failed reason=budget reason_stop={reason_stop}", ReasonStop.SAFE_STOP_WINDOW_EXPIRED);
            return FinalizeAndReturn(Outcome(
                ReasonStop.SAFE_STOP_WINDOW_EXPIRED,
                state.LastLoadedContestId,
                null,
                0,
                state.LastLoadedContestId,
                deadlineSeconds), startUtc, startTimestamp);
        }
        catch (LotodicasApiAuthException ex)
        {
            _log.LogError(ex, "sync.all.failed reason=auth reason_stop={reason_stop}", ReasonStop.HARD_FAIL_API_AUTH);
            return FinalizeAndReturn(Outcome(
                ReasonStop.HARD_FAIL_API_AUTH,
                state.LastLoadedContestId,
                null,
                0,
                state.LastLoadedContestId,
                deadlineSeconds), startUtc, startTimestamp);
        }
        catch (OperationCanceledException) when (hostCt.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException) when (budgetCt.IsCancellationRequested)
        {
            _log.LogWarning("sync.all.failed reason=budget_cancel reason_stop={reason_stop}", ReasonStop.SAFE_STOP_WINDOW_EXPIRED);
            return FinalizeAndReturn(Outcome(
                ReasonStop.SAFE_STOP_WINDOW_EXPIRED,
                state.LastLoadedContestId,
                null,
                0,
                state.LastLoadedContestId,
                deadlineSeconds), startUtc, startTimestamp);
        }

        Dictionary<int, object> drawsById;
        try
        {
            drawsById = ParseBulkDraws(rawAll);
        }
        catch (JsonException ex)
        {
            _log.LogError(ex, "sync.all.failed reason=schema reason_stop={reason_stop}", ReasonStop.HARD_FAIL_API_SCHEMA);
            return FinalizeAndReturn(Outcome(
                ReasonStop.HARD_FAIL_API_SCHEMA,
                state.LastLoadedContestId,
                null,
                0,
                state.LastLoadedContestId,
                deadlineSeconds), startUtc, startTimestamp);
        }
        catch (InvalidOperationException ex)
        {
            _log.LogError(ex, "sync.all.failed reason=schema reason_stop={reason_stop}", ReasonStop.HARD_FAIL_API_SCHEMA);
            return FinalizeAndReturn(Outcome(
                ReasonStop.HARD_FAIL_API_SCHEMA,
                state.LastLoadedContestId,
                null,
                0,
                state.LastLoadedContestId,
                deadlineSeconds), startUtc, startTimestamp);
        }

        EmitEvent("sync.all.ok", new ActivityTagsCollection { ["draws_count"] = drawsById.Count });
        _log.LogDebug("sync.all.ok draws_count={draws_count}", drawsById.Count);

        var latestId = drawsById.Count == 0 ? 0 : drawsById.Keys.Max();
        var persistedLastId = ComputeMaxContiguousContestId(drawsById.Keys);
        string? persistedDrawDate = null;
        if (persistedLastId > 0 && drawsById.TryGetValue(persistedLastId, out var persistedDraw))
        {
            persistedDrawDate = _catalog.GetDrawDateFromDraw(persistedDraw);
        }

        var newDoc = _catalog.MergeOrderedDraws(drawsById);

        _log.LogDebug("persist.blob.start persisted_last_id={persisted_last_id}", persistedLastId);
        EmitEvent("persist.blob.start");
        await _blob.WriteRawAsync(newDoc, budgetCt);
        EmitEvent("persist.blob.ok");

        var newState = state with
        {
            LastLoadedContestId = persistedLastId,
            LastLoadedDrawDate = persistedDrawDate,
            LastUpdatedAtUtc = _clock.UtcNow
        };

        _log.LogDebug("persist.state.start persisted_last_id={persisted_last_id}", persistedLastId);
        EmitEvent("persist.state.start");
        await _state.WriteRawAsync(newState, budgetCt);
        EmitEvent("persist.state.ok");

        return FinalizeAndReturn(new UpdateLoteriaResultsOutcome(
            ModalityKey: _modalityKey,
            ReasonStop: ReasonStop.COMPLETED_SUCCESS,
            LastLoadedContestId: state.LastLoadedContestId,
            LatestId: latestId,
            ProcessedCount: drawsById.Count,
            PersistedLastId: persistedLastId,
            DeadlineSeconds: deadlineSeconds,
            Timezone: "America/Sao_Paulo"
        ), startUtc, startTimestamp);
    }

    private Dictionary<int, object> ParseBulkDraws(object rawAll)
    {
        var drawsById = new Dictionary<int, object>();
        foreach (var rawContest in EnumerateBulkContests(rawAll))
        {
            var draw = _catalog.ParseContestToDraw(rawContest);
            drawsById[_catalog.GetContestIdFromDraw(draw)] = draw;
        }

        return drawsById;
    }

    private static int ComputeMaxContiguousContestId(IEnumerable<int> contestIds)
    {
        var set = contestIds.ToHashSet();
        if (set.Count == 0)
        {
            return 0;
        }

        var id = 1;
        while (set.Contains(id))
        {
            id++;
        }

        return id - 1;
    }

    private UpdateLoteriaResultsOutcome FinalizeAndReturn(UpdateLoteriaResultsOutcome outcome, DateTimeOffset startUtc, long startTimestamp)
    {
        var elapsedSeconds = Stopwatch.GetElapsedTime(startTimestamp).TotalSeconds;
        var ctx = _runContext.Current;

        Activity.Current?.SetTag("reason_stop", outcome.ReasonStop.ToString());
        Activity.Current?.SetTag("last_loaded_contest_id", outcome.LastLoadedContestId);
        Activity.Current?.SetTag("latest_id", outcome.LatestId);
        Activity.Current?.SetTag("processed_count", outcome.ProcessedCount);
        Activity.Current?.SetTag("persisted_last_id", outcome.PersistedLastId);
        Activity.Current?.SetTag("retries_count", ctx?.RetriesCount ?? 0);
        Activity.Current?.SetTag("rate_limit_wait_seconds_total", ctx?.RateLimitWaitSecondsTotal ?? 0d);
        Activity.Current?.SetTag("elapsed_seconds", elapsedSeconds);
        EmitEvent("stop", new ActivityTagsCollection { ["reason_stop"] = outcome.ReasonStop.ToString() });

        _log.LogDebug(
            "update_results.stop reason_stop={reason_stop} last_loaded_contest_id={last_loaded_contest_id} latest_id={latest_id} processed_count={processed_count} persisted_last_id={persisted_last_id} retries_count={retries_count} rate_limit_wait_seconds_total={rate_limit_wait_seconds_total} elapsed_seconds={elapsed_seconds}",
            outcome.ReasonStop,
            outcome.LastLoadedContestId,
            outcome.LatestId,
            outcome.ProcessedCount,
            outcome.PersistedLastId,
            ctx?.RetriesCount ?? 0,
            ctx?.RateLimitWaitSecondsTotal ?? 0d,
            elapsedSeconds);

        return outcome;
    }

    private Activity? StartRootActivity(RunContextSnapshot? ctx, int deadlineSeconds)
    {
        var a = LotofacilLoaderActivitySource.Instance.StartActivity("LotofacilLoader.UpdateResults", ActivityKind.Internal);
        if (a is null)
        {
            return null;
        }

        a.SetTag("run_id", ctx?.RunId);
        a.SetTag("modality", _modalityKey);
        a.SetTag("timezone", "America/Sao_Paulo");
        a.SetTag("deadline_seconds", deadlineSeconds);
        return a;
    }

    private static void EmitEvent(string name, ActivityTagsCollection? tags = null)
    {
        var a = Activity.Current;
        if (a is null)
        {
            return;
        }

        a.AddEvent(new ActivityEvent(name, tags: tags));
    }

    private UpdateLoteriaResultsOutcome Outcome(
        ReasonStop reason,
        int lastLoaded,
        int? latestId,
        int processed,
        int persistedLast,
        int deadlineSeconds) =>
        new(
            ModalityKey: _modalityKey,
            ReasonStop: reason,
            LastLoadedContestId: lastLoaded,
            LatestId: latestId,
            ProcessedCount: processed,
            PersistedLastId: persistedLast,
            DeadlineSeconds: deadlineSeconds,
            Timezone: "America/Sao_Paulo");

    private Dictionary<int, object> ToDrawMap(object document)
    {
        var parsed = _catalog.ParseDocument(document);
        return parsed switch
        {
            LotofacilBlobDocument lf => lf.Draws.ToDictionary(d => d.ContestId, d => (object)d),
            MegaSenaBlobDocument ms => ms.Draws.ToDictionary(d => d.ContestId, d => (object)d),
            QuinaBlobDocument qn => qn.Draws.ToDictionary(d => d.ContestId, d => (object)d),
            _ => throw new InvalidOperationException("Documento de blob não suportado para este catálogo.")
        };
    }

    private static bool HasMinimumBudget(IExecutionBudget budget) =>
        budget.HasMinimumBudget(TimeSpan.FromSeconds(15));

    private async Task<LoteriaLoaderState> ReadOrInitializeStateAsync(IExecutionBudget budget, CancellationToken ct)
    {
        var raw = await _state.TryReadRawAsync(ct);
        if (raw is not null)
        {
            return ParseState(raw);
        }

        var doc = await ReadBlobDocumentAsync(ct);
        var drawsMap = ToDrawMap(doc);
        var max = drawsMap.Count == 0 ? (int?)null : ComputeMaxContiguousContestId(drawsMap.Keys);

        string? maxDate = null;
        if (max is > 0 && drawsMap.TryGetValue(max.Value, out var maxDraw))
        {
            maxDate = _catalog.GetDrawDateFromDraw(maxDraw);
        }

        if (max is null)
        {
            return new LoteriaLoaderState(
                LastLoadedContestId: 0,
                LastLoadedDrawDate: null,
                LastUpdatedAtUtc: _clock.UtcNow,
                ETag: null
            );
        }

        var init = new LoteriaLoaderState(
            LastLoadedContestId: max ?? 0,
            LastLoadedDrawDate: maxDate,
            LastUpdatedAtUtc: _clock.UtcNow,
            ETag: null
        );

        if (!HasMinimumBudget(budget))
        {
            return init;
        }

        await _state.WriteRawAsync(init, ct);
        return init;
    }

    private async Task<object> ReadBlobDocumentAsync(CancellationToken ct)
    {
        var raw = await _blob.TryReadRawAsync(ct);
        if (raw is null)
        {
            return _catalog.EmptyDocument();
        }

        return _catalog.ParseDocument(raw);
    }

    private static IEnumerable<object> EnumerateBulkContests(object rawAll)
    {
        var root = ToRootElement(rawAll);
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("Payload /results/all inválido: data[] ausente.");
        }

        foreach (var item in data.EnumerateArray())
        {
            yield return JsonSerializer.Serialize(new { data = item }, JsonOptions());
        }
    }

    private LoteriaLoaderState ParseState(object raw)
    {
        if (raw is LoteriaLoaderState st)
        {
            return st;
        }

        if (raw is string s)
        {
            return JsonSerializer.Deserialize<LoteriaLoaderState>(s, JsonOptions()) ??
                   new LoteriaLoaderState(0, null, DateTimeOffset.MinValue, null);
        }

        if (raw is JsonDocument jd)
        {
            return jd.RootElement.Deserialize<LoteriaLoaderState>(JsonOptions()) ??
                   new LoteriaLoaderState(0, null, DateTimeOffset.MinValue, null);
        }

        if (raw is JsonElement je)
        {
            return je.Deserialize<LoteriaLoaderState>(JsonOptions()) ??
                   new LoteriaLoaderState(0, null, DateTimeOffset.MinValue, null);
        }

        var json = JsonSerializer.Serialize(raw, JsonOptions());
        return JsonSerializer.Deserialize<LoteriaLoaderState>(json, JsonOptions()) ??
               new LoteriaLoaderState(0, null, DateTimeOffset.MinValue, null);
    }

    private static JsonSerializerOptions JsonOptions() => new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static JsonElement ToRootElement(object raw)
    {
        if (raw is JsonDocument jd)
        {
            return jd.RootElement.Clone();
        }

        if (raw is JsonElement je)
        {
            return je.Clone();
        }

        if (raw is string s)
        {
            using var doc = JsonDocument.Parse(s);
            return doc.RootElement.Clone();
        }

        var json = JsonSerializer.Serialize(raw, JsonOptions());
        using var d = JsonDocument.Parse(json);
        return d.RootElement.Clone();
    }
}
