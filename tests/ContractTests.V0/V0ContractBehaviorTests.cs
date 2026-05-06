using System.Text.Json;
using Lotofacil.Loader.Application;
using Lotofacil.Loader.Domain;
using Lotofacil.Loader.V0.Contract;
using Xunit;

namespace ContractTests.V0;

public sealed class V0ContractBehaviorTests
{
    [Fact]
    public async Task EarlyExit_not_business_day_does_not_call_api_or_persist()
    {
        var clock = new FakeClock(Utc("2026-04-26T23:00:00Z")); // Domingo
        var delay = new FakeDelay(clock);
        var api = new FakeApi(latestId: 10);
        var seq = new EventSequencer();
        var blob = new InMemoryBlobStore(seq);
        var state = new InMemoryStateStore(seq, existing: new LoteriaLoaderState(5, null, clock.UtcNow, null));

        await EntryPoint.RunAsync(api, blob, state, clock, delay, CancellationToken.None);

        Assert.Empty(api.Calls);
        Assert.Empty(blob.Events);
        Assert.Empty(state.Events);
    }

    [Fact]
    public async Task EarlyExit_before_20h_does_not_call_api_or_persist()
    {
        // Segunda-feira 19:00 em São Paulo ~ 22:00Z (dependendo de DST; hoje não há DST no Brasil).
        // Vamos fixar um UTC que converte para 19:00 -03.
        var clock = new FakeClock(Utc("2026-04-27T22:00:00Z"));
        var delay = new FakeDelay(clock);
        var api = new FakeApi(latestId: 10);
        var seq = new EventSequencer();
        var blob = new InMemoryBlobStore(seq);
        var state = new InMemoryStateStore(seq, existing: new LoteriaLoaderState(5, null, clock.UtcNow, null));

        await EntryPoint.RunAsync(api, blob, state, clock, delay, CancellationToken.None);

        Assert.Empty(api.Calls);
        Assert.Empty(blob.Events);
        Assert.Empty(state.Events);
    }

    [Fact]
    public async Task DisableBusinessDayGuard_true_weekend_after_20h_does_not_early_exit_not_business_day()
    {
        // Domingo 20:30 em São Paulo => 23:30Z.
        var clock = new FakeClock(Utc("2026-04-26T23:30:00Z"));
        var delay = new FakeDelay(clock);
        var api = new FakeApi(latestId: 10);
        var seq = new EventSequencer();
        var blob = new InMemoryBlobStore(seq, existing: BlobWithOneDraw(5, "2026-04-20"));
        var state = new InMemoryStateStore(seq, existing: new LoteriaLoaderState(5, "2026-04-20", clock.UtcNow, null));

        await EntryPoint.RunAsync(
            api,
            blob,
            state,
            clock,
            delay,
            disableBusinessDayGuard: true,
            disable20hGuard: false,
            CancellationToken.None);

        Assert.Contains(api.Calls, c => c == "GetLatest:lotofacil");
    }

    [Fact]
    public async Task Disable20hGuard_true_business_day_before_20h_does_not_early_exit_before_20h()
    {
        // Segunda-feira 19:00 em São Paulo ~ 22:00Z.
        var clock = new FakeClock(Utc("2026-04-27T22:00:00Z"));
        var delay = new FakeDelay(clock);
        var api = new FakeApi(latestId: 10);
        var seq = new EventSequencer();
        var blob = new InMemoryBlobStore(seq, existing: BlobWithOneDraw(5, "2026-04-20"));
        var state = new InMemoryStateStore(seq, existing: new LoteriaLoaderState(5, "2026-04-20", clock.UtcNow, null));

        await EntryPoint.RunAsync(
            api,
            blob,
            state,
            clock,
            delay,
            disableBusinessDayGuard: false,
            disable20hGuard: true,
            CancellationToken.None);

        Assert.Contains(api.Calls, c => c == "GetLatest:lotofacil");
    }

    [Fact]
    public async Task Toggles_are_independent_disable_business_day_only_still_stops_before_20h()
    {
        // Domingo 19:00 em São Paulo => 22:00Z.
        var clock = new FakeClock(Utc("2026-04-26T22:00:00Z"));
        var delay = new FakeDelay(clock);
        var api = new FakeApi(latestId: 10);
        var seq = new EventSequencer();
        var blob = new InMemoryBlobStore(seq, existing: BlobWithOneDraw(5, "2026-04-20"));
        var state = new InMemoryStateStore(seq, existing: new LoteriaLoaderState(5, "2026-04-20", clock.UtcNow, null));

        await EntryPoint.RunAsync(
            api,
            blob,
            state,
            clock,
            delay,
            disableBusinessDayGuard: true,
            disable20hGuard: false,
            CancellationToken.None);

        Assert.Empty(api.Calls);
        Assert.Empty(blob.Events);
        Assert.Empty(state.Events);
    }

    [Fact]
    public async Task Toggles_are_independent_disable_20h_only_still_stops_on_weekend()
    {
        // Domingo 20:30 em São Paulo => 23:30Z.
        var clock = new FakeClock(Utc("2026-04-26T23:30:00Z"));
        var delay = new FakeDelay(clock);
        var api = new FakeApi(latestId: 10);
        var seq = new EventSequencer();
        var blob = new InMemoryBlobStore(seq, existing: BlobWithOneDraw(5, "2026-04-20"));
        var state = new InMemoryStateStore(seq, existing: new LoteriaLoaderState(5, "2026-04-20", clock.UtcNow, null));

        await EntryPoint.RunAsync(
            api,
            blob,
            state,
            clock,
            delay,
            disableBusinessDayGuard: false,
            disable20hGuard: true,
            CancellationToken.None);

        Assert.Empty(api.Calls);
        Assert.Empty(blob.Events);
        Assert.Empty(state.Events);
    }

    [Fact]
    public async Task Toggles_combined_both_true_allows_weekend_before_20h()
    {
        // Domingo 19:00 em São Paulo => 22:00Z.
        var clock = new FakeClock(Utc("2026-04-26T22:00:00Z"));
        var delay = new FakeDelay(clock);
        var api = new FakeApi(latestId: 10);
        var seq = new EventSequencer();
        var blob = new InMemoryBlobStore(seq, existing: BlobWithOneDraw(5, "2026-04-20"));
        var state = new InMemoryStateStore(seq, existing: new LoteriaLoaderState(5, "2026-04-20", clock.UtcNow, null));

        await EntryPoint.RunAsync(
            api,
            blob,
            state,
            clock,
            delay,
            disableBusinessDayGuard: true,
            disable20hGuard: true,
            CancellationToken.None);

        Assert.Contains(api.Calls, c => c == "GetLatest:lotofacil");
    }

    [Fact]
    public async Task EarlyExit_already_loaded_today_after_20h_does_not_call_api()
    {
        // Segunda-feira 20:30 em São Paulo => 23:30Z.
        var clock = new FakeClock(Utc("2026-04-27T23:30:00Z"));
        var delay = new FakeDelay(clock);
        var api = new FakeApi(latestId: 999);
        var seq = new EventSequencer();
        var blob = new InMemoryBlobStore(seq);
        var state = new InMemoryStateStore(seq, existing: new LoteriaLoaderState(123, "2026-04-27", clock.UtcNow, null));

        await EntryPoint.RunAsync(api, blob, state, clock, delay, CancellationToken.None);

        Assert.Empty(api.Calls);
        Assert.Empty(blob.Events);
        Assert.Empty(state.Events);
    }

    [Fact]
    public async Task Aligned_latestId_lte_lastLoaded_calls_last_once_and_does_not_persist()
    {
        var clock = new FakeClock(Utc("2026-04-27T23:30:00Z"));
        var delay = new FakeDelay(clock);
        var api = new FakeApi(latestId: 10);
        var seq = new EventSequencer();
        var blob = new InMemoryBlobStore(seq, existing: BlobWithOneDraw(10, "2026-04-27"));
        var state = new InMemoryStateStore(seq, existing: new LoteriaLoaderState(10, null, clock.UtcNow, null));

        await EntryPoint.RunAsync(api, blob, state, clock, delay, CancellationToken.None);

        Assert.Equal(new[] { "GetLatest:lotofacil" }, api.Calls);
        Assert.Empty(blob.Events);
        Assert.Empty(state.Events);
    }

    [Fact]
    public async Task Bootstrap_blob_absent_calls_results_all_and_persists_blob_before_state()
    {
        var clock = new FakeClock(Utc("2026-04-27T23:30:00Z"));
        var delay = new FakeDelay(clock);
        var api = new FakeApi(latestId: 999)
            .WithAllResults(AllResultsJson(
                ContestItemJson(id: 1, date: "2026-04-20", winners15: 0),
                ContestItemJson(id: 2, date: "2026-04-21", winners15: 3)));

        var seq = new EventSequencer();
        var blob = InMemoryBlobStore.WithoutExistingBlob(seq);
        var state = new InMemoryStateStore(seq, existing: new LoteriaLoaderState(0, null, clock.UtcNow, null));

        await EntryPoint.RunAsync(api, blob, state, clock, delay, CancellationToken.None);

        Assert.Equal(new[] { "GetAll:lotofacil" }, api.Calls);
        Assert.Equal(new[] { 1, 2 }, blob.Current.Draws.Select(d => d.ContestId).ToArray());
        Assert.Equal(2, state.Current!.LastLoadedContestId);
        Assert.Equal("2026-04-21", state.Current.LastLoadedDrawDate);
        Assert.True(
            blob.SequenceIdOfLastWrite < state.SequenceIdOfLastWrite,
            "Contrato V0: persistir blob antes do Table state."
        );
    }

    [Fact]
    public async Task Bootstrap_blob_with_empty_draws_calls_results_all()
    {
        var clock = new FakeClock(Utc("2026-04-27T23:30:00Z"));
        var delay = new FakeDelay(clock);
        var api = new FakeApi(latestId: 999)
            .WithAllResults(AllResultsJson(
                ContestItemJson(id: 1, date: "2026-04-20", winners15: 0),
                ContestItemJson(id: 2, date: "2026-04-21", winners15: 3)));

        var seq = new EventSequencer();
        var blob = new InMemoryBlobStore(seq, existing: new LotofacilBlobDocument(Array.Empty<LotofacilBlobDraw>()));
        var state = new InMemoryStateStore(seq, existing: new LoteriaLoaderState(0, null, clock.UtcNow, null));

        await EntryPoint.RunAsync(api, blob, state, clock, delay, CancellationToken.None);

        Assert.Equal(new[] { "GetAll:lotofacil" }, api.Calls);
        Assert.Equal(new[] { 1, 2 }, blob.Current.Draws.Select(d => d.ContestId).ToArray());
        Assert.Equal(2, state.Current!.LastLoadedContestId);
    }

    [Fact]
    public async Task Blob_with_non_empty_draws_skips_results_all_and_uses_incremental()
    {
        var clock = new FakeClock(Utc("2026-04-27T23:30:00Z"));
        var delay = new FakeDelay(clock);
        var api = new FakeApi(latestId: 7)
            .WithContest(6, ContestJson(id: 6, date: "2026-04-26", winners15: 0))
            .WithContest(7, ContestJson(id: 7, date: "2026-04-27", winners15: 5));

        var seq = new EventSequencer();
        var blob = new InMemoryBlobStore(seq, existing: BlobWithOneDraw(5, "2026-04-25"));
        var state = new InMemoryStateStore(seq, existing: new LoteriaLoaderState(5, "2026-04-25", clock.UtcNow, null));

        await EntryPoint.RunAsync(api, blob, state, clock, delay, CancellationToken.None);

        Assert.Equal(new[] { "GetLatest:lotofacil", "GetById:lotofacil:6", "GetById:lotofacil:7" }, api.Calls);
    }

    [Fact]
    public async Task When_gap_exists_persists_blob_before_state()
    {
        var clock = new FakeClock(Utc("2026-04-27T23:30:00Z"));
        var delay = new FakeDelay(clock);
        var api = new FakeApi(latestId: 6)
            .WithContest(6, ContestJson(id: 6, date: "2026-04-27", winners15: 5));

        var seq = new EventSequencer();
        var blob = new InMemoryBlobStore(seq, existing: BlobWithOneDraw(5, "2026-04-25"));
        var state = new InMemoryStateStore(seq, existing: new LoteriaLoaderState(5, "2026-04-25", clock.UtcNow, null));

        await EntryPoint.RunAsync(api, blob, state, clock, delay, CancellationToken.None);

        Assert.Equal(new[] { "GetLatest:lotofacil", "GetById:lotofacil:6" }, api.Calls);
        Assert.Equal(1, blob.Events.Count(e => e.StartsWith("Write:")));
        Assert.Equal(1, state.Events.Count(e => e.StartsWith("Write:")));
        Assert.True(
            blob.SequenceIdOfLastWrite < state.SequenceIdOfLastWrite,
            "Contrato V0: persistir blob antes do Table state."
        );
    }

    [Fact]
    public async Task Window_expiry_stops_safely_and_next_run_resumes_from_checkpoint()
    {
        // Segunda-feira 20:00:00 SP => 23:00Z.
        var t0 = Utc("2026-04-27T23:00:00Z");
        var clock = new FakeClock(t0);
        var delay = new FakeDelay(clock);
        var api = new FakeApi(latestId: 25);
        for (var id = 2; id <= 25; id++)
        {
            var date = id == 25 ? "2026-04-27" : $"2026-04-{(id % 28) + 1:00}";
            api.WithContest(id, ContestJson(id, date, winners15: 0));
        }

        var seq = new EventSequencer();
        var blob = new InMemoryBlobStore(seq, existing: BlobWithOneDraw(1, "2026-04-01"));
        var state = new InMemoryStateStore(seq, existing: new LoteriaLoaderState(1, "2026-04-01", clock.UtcNow, null));

        // Execução 1: com pacing 10s e janela 180s (e orçamento mínimo de 15s), deve materializar somente 2..19 (não cabe iniciar 20).
        await EntryPoint.RunAsync(api, blob, state, clock, delay, CancellationToken.None);

        Assert.NotNull(state.Current);
        Assert.Equal(19, state.Current!.LastLoadedContestId);
        Assert.Contains(blob.Current.Draws, d => d.ContestId == 2);
        Assert.Contains(blob.Current.Draws, d => d.ContestId == 19);
        Assert.DoesNotContain(blob.Current.Draws, d => d.ContestId == 20);

        // Execução 2: avança o relógio para o próximo "tick" (ainda após 20h no dia útil) e deve retomar em 4.
        clock.SetUtcNow(t0.AddHours(1));
        await EntryPoint.RunAsync(api, blob, state, clock, delay, CancellationToken.None);

        Assert.NotNull(state.Current);
        Assert.Equal(25, state.Current!.LastLoadedContestId);
        Assert.Contains(blob.Current.Draws, d => d.ContestId == 25);
    }

    [Fact]
    public async Task Idempotency_second_run_aligned_does_not_rewrite_blob_or_state()
    {
        var clock = new FakeClock(Utc("2026-04-27T23:30:00Z"));
        var delay = new FakeDelay(clock);
        var api = new FakeApi(latestId: 2)
            .WithContest(2, ContestJson(id: 2, date: "2026-04-27", winners15: 0));

        var seq = new EventSequencer();
        var blob = new InMemoryBlobStore(seq, existing: BlobWithOneDraw(1, "2026-04-20"));
        var state = new InMemoryStateStore(seq, existing: new LoteriaLoaderState(1, "2026-04-20", clock.UtcNow, null));

        await EntryPoint.RunAsync(api, blob, state, clock, delay, CancellationToken.None);
        var writesAfterFirstRun = blob.Events.Count + state.Events.Count;

        // Ajusta o estado como se tivesse persistido e "último" não avançou.
        api.SetLatest(2);
        clock.SetUtcNow(clock.UtcNow.AddMinutes(10));
        await EntryPoint.RunAsync(api, blob, state, clock, delay, CancellationToken.None);

        Assert.Equal(writesAfterFirstRun, blob.Events.Count + state.Events.Count);
    }

    private static DateTimeOffset Utc(string iso8601Utc) =>
        DateTimeOffset.Parse(iso8601Utc, null, System.Globalization.DateTimeStyles.AssumeUniversal);

    private static string ContestJson(int id, string date, int winners15)
    {
        var obj = new
        {
            data = new
            {
                draw_number = id,
                draw_date = date,
                drawing = new { draw = new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15 } },
                prizes = new[]
                {
                    new { name = "15 acertos", winners = winners15 }
                }
            }
        };

        return JsonSerializer.Serialize(obj);
    }

    private static string ContestItemJson(int id, string date, int winners15)
    {
        var obj = new
        {
            draw_number = id,
            draw_date = date,
            drawing = new { draw = new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15 } },
            prizes = new[]
            {
                new { name = "15 acertos", winners = winners15 }
            }
        };

        return JsonSerializer.Serialize(obj);
    }

    private static string AllResultsJson(params string[] contestItemsJson)
    {
        var items = contestItemsJson.Select(static item => JsonSerializer.Deserialize<JsonElement>(item)).ToArray();
        return JsonSerializer.Serialize(new { data = items });
    }

    private static LotofacilBlobDocument BlobWithOneDraw(int contestId, string date) =>
        new(new[]
        {
            new LotofacilBlobDraw(
                ContestId: contestId,
                DrawDate: date,
                Numbers: new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15 },
                Winners15: 0,
                HasWinner15: false)
        });

    private sealed class FakeClock : IClock
    {
        public FakeClock(DateTimeOffset utcNow) => UtcNow = utcNow;
        public DateTimeOffset UtcNow { get; private set; }
        public void SetUtcNow(DateTimeOffset utcNow) => UtcNow = utcNow;
    }

    private sealed class FakeDelay : IDelay
    {
        private readonly FakeClock _clock;
        public FakeDelay(FakeClock clock) => _clock = clock;
        public List<TimeSpan> Delays { get; } = new();
        public Task DelayAsync(TimeSpan delay, CancellationToken ct)
        {
            Delays.Add(delay);
            _clock.SetUtcNow(_clock.UtcNow.Add(delay));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeApi : ILotteriesApiClient
    {
        private readonly Dictionary<int, string> _byId = new();
        private string? _allResultsRaw;
        private int _latestId;
        public FakeApi(int latestId) => _latestId = latestId;

        public List<string> Calls { get; } = new();

        public FakeApi SetLatest(int latestId)
        {
            _latestId = latestId;
            return this;
        }

        public FakeApi WithContest(int id, string rawJson)
        {
            _byId[id] = rawJson;
            return this;
        }

        public FakeApi WithAllResults(string rawJson)
        {
            _allResultsRaw = rawJson;
            return this;
        }

        public Task<int> GetLatestContestIdAsync(string lotteryApiSegment, CancellationToken ct)
        {
            Calls.Add($"GetLatest:{lotteryApiSegment}");
            return Task.FromResult(_latestId);
        }

        public Task<object> GetContestByIdRawAsync(string lotteryApiSegment, int contestId, CancellationToken ct)
        {
            Calls.Add($"GetById:{lotteryApiSegment}:{contestId}");
            if (!_byId.TryGetValue(contestId, out var raw))
            {
                throw new InvalidOperationException($"Missing fixture for contestId={contestId}");
            }
            return Task.FromResult<object>(raw);
        }

        public Task<object> GetAllResultsRawAsync(string lotteryApiSegment, CancellationToken ct)
        {
            Calls.Add($"GetAll:{lotteryApiSegment}");
            if (string.IsNullOrWhiteSpace(_allResultsRaw))
            {
                throw new InvalidOperationException("Missing fixture for /results/all");
            }

            return Task.FromResult<object>(_allResultsRaw);
        }
    }

    private sealed class InMemoryBlobStore : ILoteriaBlobStore
    {
        private readonly EventSequencer _seq;
        private bool _exists;

        public InMemoryBlobStore(EventSequencer seq, LotofacilBlobDocument? existing = null)
        {
            _seq = seq;
            _exists = true;
            Current = existing ?? new LotofacilBlobDocument(Array.Empty<LotofacilBlobDraw>());
        }

        private InMemoryBlobStore(EventSequencer seq, bool exists, LotofacilBlobDocument current)
        {
            _seq = seq;
            _exists = exists;
            Current = current;
        }

        public static InMemoryBlobStore WithoutExistingBlob(EventSequencer seq) =>
            new(seq, exists: false, current: new LotofacilBlobDocument(Array.Empty<LotofacilBlobDraw>()));

        public LotofacilBlobDocument Current { get; private set; }
        public List<string> Events { get; } = new();
        public int SequenceIdOfLastWrite { get; private set; } = -1;

        public Task<object?> TryReadRawAsync(CancellationToken ct) =>
            Task.FromResult<object?>(_exists ? Current : null);

        public Task WriteRawAsync(object document, CancellationToken ct)
        {
            Current = document switch
            {
                LotofacilBlobDocument d => d,
                string s => JsonSerializer.Deserialize<LotofacilBlobDocument>(s) ??
                            new LotofacilBlobDocument(Array.Empty<LotofacilBlobDraw>()),
                _ => throw new InvalidOperationException($"Unsupported blob document type: {document.GetType().FullName}")
            };
            _exists = true;

            SequenceIdOfLastWrite = _seq.Next();
            Events.Add($"Write:{SequenceIdOfLastWrite}");
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryStateStore : ILoteriaStateStore
    {
        private readonly EventSequencer _seq;

        public InMemoryStateStore(EventSequencer seq, LoteriaLoaderState? existing = null)
        {
            _seq = seq;
            Current = existing;
        }

        public LoteriaLoaderState? Current { get; private set; }
        public List<string> Events { get; } = new();
        public int SequenceIdOfLastWrite { get; private set; } = -1;

        public Task<object?> TryReadRawAsync(CancellationToken ct) => Task.FromResult<object?>(Current);

        public Task WriteRawAsync(object state, CancellationToken ct)
        {
            Current = state switch
            {
                LoteriaLoaderState s => s,
                string raw => JsonSerializer.Deserialize<LoteriaLoaderState>(raw),
                _ => throw new InvalidOperationException($"Unsupported state type: {state.GetType().FullName}")
            };

            SequenceIdOfLastWrite = _seq.Next();
            Events.Add($"Write:{SequenceIdOfLastWrite}");
            return Task.CompletedTask;
        }
    }

    private sealed class EventSequencer
    {
        private int _n;
        public int Next() => ++_n;
    }
}

