using System.Globalization;
using System.Text.Json;
using Lotofacil.Loader.Domain;

namespace Lotofacil.Loader.Application;

public sealed class LoteriaResultsUpdateUseCase
{
    private readonly IClock _clock;
    private readonly IDelay _delay;
    private readonly ILotteriesApiClient _api;
    private readonly ILoteriaBlobStore _blob;
    private readonly ILoteriaStateStore _state;
    private readonly ILoteriaBlobCatalog _catalog;
    private readonly string _modalityKey;
    private readonly string _lotteryApiSegment;

    public LoteriaResultsUpdateUseCase(
        IClock clock,
        IDelay delay,
        ILotteriesApiClient api,
        ILoteriaBlobStore blob,
        ILoteriaStateStore state,
        ILoteriaBlobCatalog catalog,
        string modalityKey,
        string lotteryApiSegment)
    {
        _clock = clock;
        _delay = delay;
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
        var nowUtc = _clock.UtcNow;
        const int deadlineSeconds = 180;
        var deadlineUtc = nowUtc.AddSeconds(deadlineSeconds);

        var nowLocal = ConvertToSaoPaulo(nowUtc);
        var todayLocal = DateOnly.FromDateTime(nowLocal.DateTime);

        if (!IsBusinessDay(todayLocal))
        {
            return Outcome(ReasonStop.EARLY_EXIT_NOT_BUSINESS_DAY, 0, null, 0, 0, deadlineSeconds);
        }

        if (!HasPassed20h(nowLocal, todayLocal))
        {
            return Outcome(ReasonStop.EARLY_EXIT_BEFORE_20H, 0, null, 0, 0, deadlineSeconds);
        }

        var state = await ReadOrInitializeStateAsync(deadlineUtc, ct);

        if (state.LastLoadedDrawDate is not null &&
            string.Equals(state.LastLoadedDrawDate, todayLocal.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), StringComparison.Ordinal))
        {
            return Outcome(
                ReasonStop.EARLY_EXIT_ALREADY_LOADED_TODAY,
                state.LastLoadedContestId,
                null,
                0,
                state.LastLoadedContestId,
                deadlineSeconds);
        }

        if (!HasMinimumBudget(deadlineUtc))
        {
            return Outcome(
                ReasonStop.SAFE_STOP_WINDOW_EXPIRED,
                state.LastLoadedContestId,
                null,
                0,
                state.LastLoadedContestId,
                deadlineSeconds);
        }

        var doc = await ReadBlobDocumentAsync(ct);
        var drawsById = ToDrawMap(doc);
        if (drawsById.Count == 0)
        {
            return await ExecuteBootstrapAsync(state, deadlineSeconds, ct);
        }

        var latestId = await _api.GetLatestContestIdAsync(_lotteryApiSegment, ct);
        if (latestId <= state.LastLoadedContestId)
        {
            return Outcome(
                ReasonStop.EARLY_EXIT_ALREADY_ALIGNED,
                state.LastLoadedContestId,
                latestId,
                0,
                state.LastLoadedContestId,
                deadlineSeconds);
        }

        var nextId = state.LastLoadedContestId + 1;
        var lastPersistableId = state.LastLoadedContestId;
        string? lastPersistableDrawDate = state.LastLoadedDrawDate;

        DateTimeOffset? lastRequestStartUtc = null;
        var processedCount = 0;

        for (var id = nextId; id <= latestId; id++)
        {
            if (!HasMinimumBudget(deadlineUtc))
            {
                break;
            }

            if (lastRequestStartUtc is not null)
            {
                var earliest = lastRequestStartUtc.Value.AddSeconds(10);
                var wait = earliest - _clock.UtcNow;
                if (wait > TimeSpan.Zero)
                {
                    if (_clock.UtcNow.Add(wait) >= deadlineUtc)
                    {
                        break;
                    }

                    await _delay.DelayAsync(wait, ct);
                }
            }

            lastRequestStartUtc = _clock.UtcNow;

            object draw;
            try
            {
                var raw = await _api.GetContestByIdRawAsync(_lotteryApiSegment, id, ct);
                draw = _catalog.ParseContestToDraw(raw);
            }
            catch
            {
                break;
            }

            drawsById[_catalog.GetContestIdFromDraw(draw)] = draw;
            lastPersistableId = id;
            lastPersistableDrawDate = _catalog.GetDrawDateFromDraw(draw);
            processedCount++;
        }

        if (lastPersistableId == state.LastLoadedContestId)
        {
            return Outcome(
                ReasonStop.SAFE_STOP_WINDOW_EXPIRED,
                state.LastLoadedContestId,
                latestId,
                processedCount,
                state.LastLoadedContestId,
                deadlineSeconds);
        }

        var newDoc = _catalog.MergeOrderedDraws(drawsById);

        await _blob.WriteRawAsync(newDoc, ct);

        var newState = state with
        {
            LastLoadedContestId = lastPersistableId,
            LastLoadedDrawDate = lastPersistableDrawDate,
            LastUpdatedAtUtc = _clock.UtcNow
        };

        await _state.WriteRawAsync(newState, ct);

        return new UpdateLoteriaResultsOutcome(
            ModalityKey: _modalityKey,
            ReasonStop: ReasonStop.COMPLETED_SUCCESS,
            LastLoadedContestId: state.LastLoadedContestId,
            LatestId: latestId,
            ProcessedCount: processedCount,
            PersistedLastId: lastPersistableId,
            DeadlineSeconds: deadlineSeconds,
            Timezone: "America/Sao_Paulo"
        );
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
            _ => throw new InvalidOperationException("Documento de blob não suportado para este catálogo.")
        };
    }

    private bool HasMinimumBudget(DateTimeOffset deadlineUtc) =>
        deadlineUtc - _clock.UtcNow >= TimeSpan.FromSeconds(15);

    private static bool IsBusinessDay(DateOnly date)
    {
        var dow = date.DayOfWeek;
        return dow is not DayOfWeek.Saturday and not DayOfWeek.Sunday;
    }

    private static bool HasPassed20h(DateTimeOffset nowLocal, DateOnly todayLocal)
    {
        var cutoff = todayLocal.ToDateTime(new TimeOnly(20, 0, 0));
        return nowLocal.DateTime >= cutoff;
    }

    private static DateTimeOffset ConvertToSaoPaulo(DateTimeOffset utcNow)
    {
        TimeZoneInfo tz;
        try
        {
            tz = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");
        }
        catch (TimeZoneNotFoundException)
        {
            tz = TimeZoneInfo.FindSystemTimeZoneById("E. South America Standard Time");
        }

        return TimeZoneInfo.ConvertTime(utcNow, tz);
    }

    private async Task<LoteriaLoaderState> ReadOrInitializeStateAsync(DateTimeOffset deadlineUtc, CancellationToken ct)
    {
        var raw = await _state.TryReadRawAsync(ct);
        if (raw is not null)
        {
            return ParseState(raw);
        }

        var doc = await ReadBlobDocumentAsync(ct);
        var drawsMap = ToDrawMap(doc);
        var max = drawsMap.Count == 0 ? (int?)null : drawsMap.Keys.Max();

        string? maxDate = null;
        if (max is not null && drawsMap.TryGetValue(max.Value, out var maxDraw))
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

        if (!HasMinimumBudget(deadlineUtc))
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

    private async Task<UpdateLoteriaResultsOutcome> ExecuteBootstrapAsync(
        LoteriaLoaderState state,
        int deadlineSeconds,
        CancellationToken ct)
    {
        var rawAll = await _api.GetAllResultsRawAsync(_lotteryApiSegment, ct);
        var drawsById = new Dictionary<int, object>();
        foreach (var rawContest in EnumerateBulkContests(rawAll))
        {
            var draw = _catalog.ParseContestToDraw(rawContest);
            drawsById[_catalog.GetContestIdFromDraw(draw)] = draw;
        }

        var bootstrapDoc = _catalog.MergeOrderedDraws(drawsById);
        await _blob.WriteRawAsync(bootstrapDoc, ct);

        var maxContestId = drawsById.Count == 0 ? 0 : drawsById.Keys.Max();
        string? maxDrawDate = null;
        if (drawsById.TryGetValue(maxContestId, out var maxDraw))
        {
            maxDrawDate = _catalog.GetDrawDateFromDraw(maxDraw);
        }

        var bootstrapState = state with
        {
            LastLoadedContestId = maxContestId,
            LastLoadedDrawDate = maxDrawDate,
            LastUpdatedAtUtc = _clock.UtcNow
        };

        await _state.WriteRawAsync(bootstrapState, ct);

        return new UpdateLoteriaResultsOutcome(
            ModalityKey: _modalityKey,
            ReasonStop: ReasonStop.COMPLETED_SUCCESS,
            LastLoadedContestId: state.LastLoadedContestId,
            LatestId: null,
            ProcessedCount: drawsById.Count,
            PersistedLastId: maxContestId,
            DeadlineSeconds: deadlineSeconds,
            Timezone: "America/Sao_Paulo"
        );
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
