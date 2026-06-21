using Lotofacil.Loader.Application;
using Lotofacil.Loader.Domain;
using Lotofacil.Loader.V0.Contract;
using Xunit;

namespace ContractTests.V0;

public sealed class V0ContractBehaviorTests
{
    [Fact]
    public async Task EarlyExit_already_loaded_today_does_not_call_api()
    {
        // Segunda-feira 20:30 em São Paulo => 23:30Z.
        var clock = new FakeClock(ContractTestHarness.Utc("2026-04-27T23:30:00Z"));
        var delay = new FakeDelay(clock);
        var api = new FakeApi(latestId: 999);
        var seq = new EventSequencer();
        var blob = new InMemoryBlobStore(seq, ContractTestHarness.BlobWithDraws((123, "2026-04-27")));
        var state = new InMemoryStateStore(seq, existing: new LoteriaLoaderState(123, "2026-04-27", clock.UtcNow, null));

        await EntryPoint.RunAsync(api, blob, state, clock, delay, CancellationToken.None);

        Assert.Empty(api.Calls);
        Assert.Empty(blob.Events);
        Assert.Empty(state.Events);
    }

    [Fact]
    public async Task Aligned_latestId_lte_lastLoaded_calls_last_once_and_does_not_persist()
    {
        var clock = new FakeClock(ContractTestHarness.Utc("2026-04-27T23:30:00Z"));
        var delay = new FakeDelay(clock);
        var api = new FakeApi(latestId: 10);
        var seq = new EventSequencer();
        var blob = new InMemoryBlobStore(seq, ContractTestHarness.BlobWithDraws((10, "2026-04-27")));
        var state = new InMemoryStateStore(seq, existing: new LoteriaLoaderState(10, null, clock.UtcNow, null));

        await EntryPoint.RunAsync(api, blob, state, clock, delay, CancellationToken.None);

        Assert.Equal(new[] { "GetLatest:lotofacil" }, api.Calls);
        Assert.Empty(blob.Events);
        Assert.Empty(state.Events);
    }

    [Fact]
    public async Task Bootstrap_blob_absent_calls_results_all_and_persists_blob_before_state()
    {
        var clock = new FakeClock(ContractTestHarness.Utc("2026-04-27T23:30:00Z"));
        var delay = new FakeDelay(clock);
        var api = new FakeApi(latestId: 999)
            .WithAllResults(ContractTestHarness.AllResultsJson(
                ContractTestHarness.ContestItemJson(id: 1, date: "2026-04-20", winners15: 0),
                ContractTestHarness.ContestItemJson(id: 2, date: "2026-04-21", winners15: 3)));

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
        var clock = new FakeClock(ContractTestHarness.Utc("2026-04-27T23:30:00Z"));
        var delay = new FakeDelay(clock);
        var api = new FakeApi(latestId: 999)
            .WithAllResults(ContractTestHarness.AllResultsJson(
                ContractTestHarness.ContestItemJson(id: 1, date: "2026-04-20", winners15: 0),
                ContractTestHarness.ContestItemJson(id: 2, date: "2026-04-21", winners15: 3)));

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
        var clock = new FakeClock(ContractTestHarness.Utc("2026-04-27T23:30:00Z"));
        var delay = new FakeDelay(clock);
        var api = new FakeApi(latestId: 7)
            .WithContest(6, ContractTestHarness.ContestJson(id: 6, date: "2026-04-26", winners15: 0))
            .WithContest(7, ContractTestHarness.ContestJson(id: 7, date: "2026-04-27", winners15: 5));

        var seq = new EventSequencer();
        var blob = new InMemoryBlobStore(seq, ContractTestHarness.BlobWithDraws((5, "2026-04-25")));
        var state = new InMemoryStateStore(seq, existing: new LoteriaLoaderState(5, "2026-04-25", clock.UtcNow, null));

        await EntryPoint.RunAsync(api, blob, state, clock, delay, CancellationToken.None);

        Assert.Equal(new[] { "GetLatest:lotofacil", "GetById:lotofacil:6", "GetById:lotofacil:7" }, api.Calls);
    }

    [Fact]
    public async Task When_gap_exists_persists_blob_before_state()
    {
        var clock = new FakeClock(ContractTestHarness.Utc("2026-04-27T23:30:00Z"));
        var delay = new FakeDelay(clock);
        var api = new FakeApi(latestId: 6)
            .WithContest(6, ContractTestHarness.ContestJson(id: 6, date: "2026-04-27", winners15: 5));

        var seq = new EventSequencer();
        var blob = new InMemoryBlobStore(seq, ContractTestHarness.BlobWithDraws((5, "2026-04-25")));
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
        var t0 = ContractTestHarness.Utc("2026-04-27T23:00:00Z");
        var clock = new FakeClock(t0);
        var delay = new FakeDelay(clock);
        var api = new FakeApi(latestId: 25);
        for (var id = 2; id <= 25; id++)
        {
            var date = id == 25 ? "2026-04-27" : $"2026-04-{(id % 28) + 1:00}";
            api.WithContest(id, ContractTestHarness.ContestJson(id, date, winners15: 0));
        }

        var seq = new EventSequencer();
        var blob = new InMemoryBlobStore(seq, ContractTestHarness.BlobWithDraws((1, "2026-04-01")));
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
        var clock = new FakeClock(ContractTestHarness.Utc("2026-04-27T23:30:00Z"));
        var delay = new FakeDelay(clock);
        var api = new FakeApi(latestId: 2)
            .WithContest(2, ContractTestHarness.ContestJson(id: 2, date: "2026-04-27", winners15: 0));

        var seq = new EventSequencer();
        var blob = new InMemoryBlobStore(seq, ContractTestHarness.BlobWithDraws((1, "2026-04-20")));
        var state = new InMemoryStateStore(seq, existing: new LoteriaLoaderState(1, "2026-04-20", clock.UtcNow, null));

        await EntryPoint.RunAsync(api, blob, state, clock, delay, CancellationToken.None);
        var writesAfterFirstRun = blob.Events.Count + state.Events.Count;

        // Ajusta o estado como se tivesse persistido e "último" não avançou.
        api.SetLatest(2);
        clock.SetUtcNow(clock.UtcNow.AddMinutes(10));
        await EntryPoint.RunAsync(api, blob, state, clock, delay, CancellationToken.None);

        Assert.Equal(writesAfterFirstRun, blob.Events.Count + state.Events.Count);
    }
}

