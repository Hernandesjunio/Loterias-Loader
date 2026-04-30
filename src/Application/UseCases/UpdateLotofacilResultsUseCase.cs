using System.Globalization;
using System.Text.Json;
using Lotofacil.Loader.Domain;

namespace Lotofacil.Loader.Application;

public sealed class UpdateLotofacilResultsUseCase
{
    private readonly IClock _clock;
    private readonly IDelay _delay;
    private readonly ILotofacilApiClient _api;
    private readonly ILotofacilBlobStore _blob;
    private readonly ILotofacilStateStore _state;

    public UpdateLotofacilResultsUseCase(
        IClock clock,
        IDelay delay,
        ILotofacilApiClient api,
        ILotofacilBlobStore blob,
        ILotofacilStateStore state)
    {
        _clock = clock;
        _delay = delay;
        _api = api;
        _blob = blob;
        _state = state;
    }

    public async Task<UpdateLotofacilResultsOutcome> ExecuteAsync(CancellationToken ct)
    {
        // Contrato V0 — docs/spec-driven-execution-guide.md (seções 10–13).
        var nowUtc = _clock.UtcNow;
        const int deadlineSeconds = 180;
        var deadlineUtc = nowUtc.AddSeconds(deadlineSeconds);

        var nowLocal = ConvertToSaoPaulo(nowUtc);
        var todayLocal = DateOnly.FromDateTime(nowLocal.DateTime);

        if (!IsBusinessDay(todayLocal))
        {
            return new UpdateLotofacilResultsOutcome(
                ReasonStop: ReasonStop.EARLY_EXIT_NOT_BUSINESS_DAY,
                LastLoadedContestId: 0,
                LatestId: null,
                ProcessedCount: 0,
                PersistedLastId: 0,
                DeadlineSeconds: deadlineSeconds,
                Timezone: "America/Sao_Paulo"
            );
        }

        if (!HasPassed20h(nowLocal, todayLocal))
        {
            return new UpdateLotofacilResultsOutcome(
                ReasonStop: ReasonStop.EARLY_EXIT_BEFORE_20H,
                LastLoadedContestId: 0,
                LatestId: null,
                ProcessedCount: 0,
                PersistedLastId: 0,
                DeadlineSeconds: deadlineSeconds,
                Timezone: "America/Sao_Paulo"
            );
        }

        var state = await ReadOrInitializeStateAsync(deadlineUtc, ct);

        // Encerramento antecipado: já carregou hoje (seção 10).
        if (state.LastLoadedDrawDate is not null &&
            string.Equals(state.LastLoadedDrawDate, todayLocal.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), StringComparison.Ordinal))
        {
            return new UpdateLotofacilResultsOutcome(
                ReasonStop: ReasonStop.EARLY_EXIT_ALREADY_LOADED_TODAY,
                LastLoadedContestId: state.LastLoadedContestId,
                LatestId: null,
                ProcessedCount: 0,
                PersistedLastId: state.LastLoadedContestId,
                DeadlineSeconds: deadlineSeconds,
                Timezone: "America/Sao_Paulo"
            );
        }

        // Antes de chamar "último": orçamento mínimo (seção 5).
        if (!HasMinimumBudget(deadlineUtc))
        {
            return new UpdateLotofacilResultsOutcome(
                ReasonStop: ReasonStop.SAFE_STOP_WINDOW_EXPIRED,
                LastLoadedContestId: state.LastLoadedContestId,
                LatestId: null,
                ProcessedCount: 0,
                PersistedLastId: state.LastLoadedContestId,
                DeadlineSeconds: deadlineSeconds,
                Timezone: "America/Sao_Paulo"
            );
        }

        var latestId = await _api.GetLatestContestIdAsync(ct);
        if (latestId <= state.LastLoadedContestId)
        {
            return new UpdateLotofacilResultsOutcome(
                ReasonStop: ReasonStop.EARLY_EXIT_ALREADY_ALIGNED,
                LastLoadedContestId: state.LastLoadedContestId,
                LatestId: latestId,
                ProcessedCount: 0,
                PersistedLastId: state.LastLoadedContestId,
                DeadlineSeconds: deadlineSeconds,
                Timezone: "America/Sao_Paulo"
            );
        }

        var doc = await ReadBlobDocumentAsync(ct);
        var drawsById = doc.Draws.ToDictionary(d => d.ContestId, d => d);

        var nextId = state.LastLoadedContestId + 1;
        var lastPersistableId = state.LastLoadedContestId;
        string? lastPersistableDrawDate = state.LastLoadedDrawDate;

        // Rate-limit/pacing mínimo (seção 12) — controlado por início de request.
        DateTimeOffset? lastRequestStartUtc = null;
        var processedCount = 0;

        for (var id = nextId; id <= latestId; id++)
        {
            if (!HasMinimumBudget(deadlineUtc))
            {
                break;
            }

            // Pacing mínimo do plano free: 10s entre inícios.
            if (lastRequestStartUtc is not null)
            {
                var earliest = lastRequestStartUtc.Value.AddSeconds(10);
                var wait = earliest - _clock.UtcNow;
                if (wait > TimeSpan.Zero)
                {
                    // Se não couber na janela, parar com segurança (seção 12/5).
                    if (_clock.UtcNow.Add(wait) >= deadlineUtc)
                    {
                        break;
                    }

                    await _delay.DelayAsync(wait, ct);
                }
            }

            lastRequestStartUtc = _clock.UtcNow;

            LotofacilBlobDraw draw;
            try
            {
                var raw = await _api.GetContestByIdRawAsync(id, ct);
                draw = ParseContestToBlobDraw(raw);
            }
            catch
            {
                // Sem classificar aqui (ports são fakes); comportamento seguro: não avançar além do último contíguo.
                break;
            }

            drawsById[draw.ContestId] = draw;
            lastPersistableId = id;
            lastPersistableDrawDate = draw.DrawDate;
            processedCount++;
        }

        if (lastPersistableId == state.LastLoadedContestId)
        {
            return new UpdateLotofacilResultsOutcome(
                ReasonStop: ReasonStop.SAFE_STOP_WINDOW_EXPIRED,
                LastLoadedContestId: state.LastLoadedContestId,
                LatestId: latestId,
                ProcessedCount: processedCount,
                PersistedLastId: state.LastLoadedContestId,
                DeadlineSeconds: deadlineSeconds,
                Timezone: "America/Sao_Paulo"
            );
        }

        // Persistência: blob primeiro, table depois (seção 13).
        var newDoc = new LotofacilBlobDocument(
            drawsById.Values
                .OrderBy(d => d.ContestId)
                .ToArray()
        );

        await _blob.WriteRawAsync(newDoc, ct);

        var newState = state with
        {
            LastLoadedContestId = lastPersistableId,
            LastLoadedDrawDate = lastPersistableDrawDate,
            LastUpdatedAtUtc = _clock.UtcNow
        };

        await _state.WriteRawAsync(newState, ct);

        return new UpdateLotofacilResultsOutcome(
            ReasonStop: ReasonStop.COMPLETED_SUCCESS,
            LastLoadedContestId: state.LastLoadedContestId,
            LatestId: latestId,
            ProcessedCount: processedCount,
            PersistedLastId: lastPersistableId,
            DeadlineSeconds: deadlineSeconds,
            Timezone: "America/Sao_Paulo"
        );
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
        // Especificação diz IANA "America/Sao_Paulo"; no Windows é comum o ID ser diferente.
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

    private async Task<LotofacilState> ReadOrInitializeStateAsync(DateTimeOffset deadlineUtc, CancellationToken ct)
    {
        var raw = await _state.TryReadRawAsync(ct);
        if (raw is not null)
        {
            var parsed = ParseState(raw);

            // Se state existe mas blob está "atrás": tratamos como falha dura no contrato.
            // Aqui apenas validamos no caminho em que também lemos blob para derivação inicial.
            return parsed;
        }

        // Primeira execução: derivar do blob se existir (seção 9).
        var doc = await ReadBlobDocumentAsync(ct);
        var max = doc.Draws.Count == 0 ? (int?)null : doc.Draws.Max(d => d.ContestId);

        var init = new LotofacilState(
            LastLoadedContestId: max ?? 0,
            LastLoadedDrawDate: max is null ? null : doc.Draws.First(d => d.ContestId == max.Value).DrawDate,
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

    private async Task<LotofacilBlobDocument> ReadBlobDocumentAsync(CancellationToken ct)
    {
        var raw = await _blob.TryReadRawAsync(ct);
        if (raw is null)
        {
            return new LotofacilBlobDocument(Array.Empty<LotofacilBlobDraw>());
        }

        return ParseBlobDocument(raw);
    }

    private static LotofacilBlobDocument ParseBlobDocument(object raw)
    {
        if (raw is LotofacilBlobDocument doc)
        {
            return doc;
        }

        if (raw is string s)
        {
            return JsonSerializer.Deserialize<LotofacilBlobDocument>(s, JsonOptions()) ??
                   new LotofacilBlobDocument(Array.Empty<LotofacilBlobDraw>());
        }

        if (raw is JsonDocument jd)
        {
            return jd.RootElement.Deserialize<LotofacilBlobDocument>(JsonOptions()) ??
                   new LotofacilBlobDocument(Array.Empty<LotofacilBlobDraw>());
        }

        if (raw is JsonElement je)
        {
            return je.Deserialize<LotofacilBlobDocument>(JsonOptions()) ??
                   new LotofacilBlobDocument(Array.Empty<LotofacilBlobDraw>());
        }

        // Fallback para fakes que já passam um POCO/anonymous.
        var json = JsonSerializer.Serialize(raw, JsonOptions());
        return JsonSerializer.Deserialize<LotofacilBlobDocument>(json, JsonOptions()) ??
               new LotofacilBlobDocument(Array.Empty<LotofacilBlobDraw>());
    }

    private static LotofacilState ParseState(object raw)
    {
        if (raw is LotofacilState st)
        {
            return st;
        }

        if (raw is string s)
        {
            return JsonSerializer.Deserialize<LotofacilState>(s, JsonOptions()) ??
                   new LotofacilState(0, null, DateTimeOffset.MinValue, null);
        }

        if (raw is JsonDocument jd)
        {
            return jd.RootElement.Deserialize<LotofacilState>(JsonOptions()) ??
                   new LotofacilState(0, null, DateTimeOffset.MinValue, null);
        }

        if (raw is JsonElement je)
        {
            return je.Deserialize<LotofacilState>(JsonOptions()) ??
                   new LotofacilState(0, null, DateTimeOffset.MinValue, null);
        }

        var json = JsonSerializer.Serialize(raw, JsonOptions());
        return JsonSerializer.Deserialize<LotofacilState>(json, JsonOptions()) ??
               new LotofacilState(0, null, DateTimeOffset.MinValue, null);
    }

    private static LotofacilBlobDraw ParseContestToBlobDraw(object raw)
    {
        JsonElement root;

        if (raw is JsonDocument jd)
        {
            root = jd.RootElement;
        }
        else if (raw is JsonElement je)
        {
            root = je;
        }
        else if (raw is string s)
        {
            using var doc = JsonDocument.Parse(s);
            root = doc.RootElement.Clone();
        }
        else
        {
            var json = JsonSerializer.Serialize(raw, JsonOptions());
            using var doc = JsonDocument.Parse(json);
            root = doc.RootElement.Clone();
        }

        // Esperado: { "data": { ... } }
        var data = root.GetProperty("data");

        var contestId = data.GetProperty("draw_number").GetInt32();
        var drawDate = data.GetProperty("draw_date").GetString() ?? throw new InvalidOperationException("draw_date null");

        var numbersArr = data.GetProperty("drawing").GetProperty("draw");
        var numbers = numbersArr.EnumerateArray().Select(x => x.GetInt32()).ToArray();

        var winners15 = 0;
        foreach (var prize in data.GetProperty("prizes").EnumerateArray())
        {
            var name = prize.GetProperty("name").GetString();
            if (string.Equals(name, "15 acertos", StringComparison.Ordinal))
            {
                winners15 = prize.GetProperty("winners").GetInt32();
                break;
            }
        }

        return new LotofacilBlobDraw(
            ContestId: contestId,
            DrawDate: drawDate,
            Numbers: numbers,
            Winners15: winners15,
            HasWinner15: winners15 > 0
        );
    }

    private static JsonSerializerOptions JsonOptions() => new()
    {
        PropertyNameCaseInsensitive = true
    };
}

