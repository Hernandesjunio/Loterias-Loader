using Lotofacil.Loader.Application;
using Lotofacil.Loader.Domain;
using Xunit;

namespace ContractTests.V0;

public sealed class TableBlobConsistencyTests
{
    [Fact]
    public async Task After_incremental_persist_table_matches_blob_checkpoint()
    {
        var clock = new FakeClock(ContractTestHarness.Utc("2026-04-27T23:30:00Z"));
        var delay = new FakeDelay(clock);
        var api = new FakeApi(latestId: 6)
            .WithContest(6, ContractTestHarness.ContestJson(id: 6, date: "2026-04-27", winners15: 5));

        var seq = new EventSequencer();
        var blob = new InMemoryBlobStore(seq, ContractTestHarness.BlobWithDraws((5, "2026-04-25")));
        var state = new InMemoryStateStore(seq, new LoteriaLoaderState(5, "2026-04-25", clock.UtcNow, null));

        var outcome = await ContractTestHarness.RunUseCaseAsync(api, blob, state, clock, delay);

        Assert.Equal(ReasonStop.COMPLETED_SUCCESS, outcome.ReasonStop);
        ContractTestHarness.AssertTableBlobConsistent(blob, state);
        ContractTestHarness.AssertBlobWrittenBeforeState(blob, state);
        Assert.Equal(6, state.Current!.LastLoadedContestId);
        Assert.Equal("2026-04-27", state.Current.LastLoadedDrawDate);
    }

    [Fact]
    public async Task After_bootstrap_persist_table_matches_blob_checkpoint()
    {
        var clock = new FakeClock(ContractTestHarness.Utc("2026-04-27T23:30:00Z"));
        var delay = new FakeDelay(clock);
        var api = new FakeApi(latestId: 999)
            .WithAllResults(ContractTestHarness.AllResultsJson(
                ContractTestHarness.ContestItemJson(id: 1, date: "2026-04-20"),
                ContractTestHarness.ContestItemJson(id: 2, date: "2026-04-21", winners15: 3)));

        var seq = new EventSequencer();
        var blob = InMemoryBlobStore.WithoutExistingBlob(seq);
        var state = new InMemoryStateStore(seq, new LoteriaLoaderState(0, null, clock.UtcNow, null));

        var outcome = await ContractTestHarness.RunUseCaseAsync(api, blob, state, clock, delay);

        Assert.Equal(ReasonStop.COMPLETED_SUCCESS, outcome.ReasonStop);
        ContractTestHarness.AssertTableBlobConsistent(blob, state);
        ContractTestHarness.AssertBlobWrittenBeforeState(blob, state);
    }

    [Fact]
    public async Task After_window_expiry_partial_persist_table_matches_blob_checkpoint()
    {
        var t0 = ContractTestHarness.Utc("2026-04-27T23:00:00Z");
        var clock = new FakeClock(t0);
        var delay = new FakeDelay(clock);
        var api = new FakeApi(latestId: 25);
        for (var id = 2; id <= 25; id++)
        {
            var date = id == 25 ? "2026-04-27" : $"2026-04-{(id % 28) + 1:00}";
            api.WithContest(id, ContractTestHarness.ContestJson(id, date));
        }

        var seq = new EventSequencer();
        var blob = new InMemoryBlobStore(seq, ContractTestHarness.BlobWithDraws((1, "2026-04-01")));
        var state = new InMemoryStateStore(seq, new LoteriaLoaderState(1, "2026-04-01", clock.UtcNow, null));

        await ContractTestHarness.RunUseCaseAsync(api, blob, state, clock, delay);

        ContractTestHarness.AssertTableBlobConsistent(blob, state);
        ContractTestHarness.AssertBlobWrittenBeforeState(blob, state);
        Assert.Equal(19, state.Current!.LastLoadedContestId);
    }

    [Fact]
    public async Task When_state_missing_initialized_from_blob_table_matches_blob_without_blob_write()
    {
        var clock = new FakeClock(ContractTestHarness.Utc("2026-04-27T23:30:00Z"));
        var delay = new FakeDelay(clock);
        var api = new FakeApi(latestId: 5);

        var seq = new EventSequencer();
        var blob = new InMemoryBlobStore(seq, ContractTestHarness.BlobWithDraws((5, "2026-04-25")));
        var state = new InMemoryStateStore(seq, existing: null);

        var outcome = await ContractTestHarness.RunUseCaseAsync(api, blob, state, clock, delay);

        Assert.Equal(ReasonStop.EARLY_EXIT_ALREADY_ALIGNED, outcome.ReasonStop);
        Assert.Empty(blob.Events);
        Assert.Equal(1, state.Events.Count(e => e.StartsWith("Write:")));
        ContractTestHarness.AssertTableBlobConsistent(blob, state);
        Assert.Equal(5, state.Current!.LastLoadedContestId);
        Assert.Equal("2026-04-25", state.Current.LastLoadedDrawDate);
    }

    [Fact]
    public async Task When_table_ahead_of_blob_hard_fails_without_api_or_persist()
    {
        var clock = new FakeClock(ContractTestHarness.Utc("2026-06-21T23:30:00Z"));
        var delay = new FakeDelay(clock);
        var api = new FakeApi(latestId: 999);

        var seq = new EventSequencer();
        var blob = new InMemoryBlobStore(seq, ContractTestHarness.BlobWithDraws((100, "2026-05-01")));
        var state = new InMemoryStateStore(
            seq,
            new LoteriaLoaderState(150, "2026-06-21", clock.UtcNow, null));

        var outcome = await ContractTestHarness.RunUseCaseAsync(api, blob, state, clock, delay);

        Assert.Equal(ReasonStop.HARD_FAIL_STATE_INCONSISTENT_TABLE_GT_BLOB, outcome.ReasonStop);
        Assert.Empty(api.Calls);
        Assert.Empty(blob.Events);
        Assert.Empty(state.Events);
    }

    [Fact]
    public async Task When_table_says_today_loaded_but_blob_lacks_today_does_not_early_exit()
    {
        var clock = new FakeClock(ContractTestHarness.Utc("2026-06-21T23:30:00Z"));
        var delay = new FakeDelay(clock);
        var api = new FakeApi(latestId: 102)
            .WithContest(101, ContractTestHarness.ContestJson(id: 101, date: "2026-06-20"))
            .WithContest(102, ContractTestHarness.ContestJson(id: 102, date: "2026-06-21", winners15: 2));

        var seq = new EventSequencer();
        var blob = new InMemoryBlobStore(seq, ContractTestHarness.BlobWithDraws((100, "2026-05-01")));
        var state = new InMemoryStateStore(
            seq,
            new LoteriaLoaderState(102, "2026-06-21", clock.UtcNow, null));

        var outcome = await ContractTestHarness.RunUseCaseAsync(api, blob, state, clock, delay);

        Assert.Equal(ReasonStop.HARD_FAIL_STATE_INCONSISTENT_TABLE_GT_BLOB, outcome.ReasonStop);
        Assert.Empty(api.Calls);
    }

    [Fact]
    public async Task When_table_says_today_and_blob_confirms_today_skips_api_calls()
    {
        var clock = new FakeClock(ContractTestHarness.Utc("2026-06-21T23:30:00Z"));
        var delay = new FakeDelay(clock);
        var api = new FakeApi(latestId: 999);

        var seq = new EventSequencer();
        var blob = new InMemoryBlobStore(
            seq,
            ContractTestHarness.BlobWithDraws((100, "2026-05-01"), (102, "2026-06-21")));
        var state = new InMemoryStateStore(
            seq,
            new LoteriaLoaderState(102, "2026-06-21", clock.UtcNow, null));

        var outcome = await ContractTestHarness.RunUseCaseAsync(api, blob, state, clock, delay);

        Assert.Equal(ReasonStop.EARLY_EXIT_ALREADY_LOADED_TODAY, outcome.ReasonStop);
        Assert.Empty(api.Calls);
        Assert.Empty(blob.Events);
        Assert.Empty(state.Events);
        ContractTestHarness.AssertTableBlobConsistent(blob, state);
    }

    [Fact]
    public async Task When_aligned_with_consistent_checkpoint_does_not_rewrite()
    {
        var clock = new FakeClock(ContractTestHarness.Utc("2026-04-27T23:30:00Z"));
        var delay = new FakeDelay(clock);
        var api = new FakeApi(latestId: 10);

        var seq = new EventSequencer();
        var blob = new InMemoryBlobStore(seq, ContractTestHarness.BlobWithDraws((10, "2026-04-25")));
        var state = new InMemoryStateStore(seq, new LoteriaLoaderState(10, "2026-04-25", clock.UtcNow, null));

        var outcome = await ContractTestHarness.RunUseCaseAsync(api, blob, state, clock, delay);

        Assert.Equal(ReasonStop.EARLY_EXIT_ALREADY_ALIGNED, outcome.ReasonStop);
        Assert.Equal(new[] { "GetLatest:lotofacil" }, api.Calls);
        Assert.Empty(blob.Events);
        Assert.Empty(state.Events);
        ContractTestHarness.AssertTableBlobConsistent(blob, state);
    }

    [Fact]
    public async Task Persist_writes_never_update_table_before_blob_in_same_run()
    {
        var clock = new FakeClock(ContractTestHarness.Utc("2026-04-27T23:30:00Z"));
        var delay = new FakeDelay(clock);
        var api = new FakeApi(latestId: 7)
            .WithContest(6, ContractTestHarness.ContestJson(id: 6, date: "2026-04-26"))
            .WithContest(7, ContractTestHarness.ContestJson(id: 7, date: "2026-04-27", winners15: 1));

        var seq = new EventSequencer();
        var blob = new InMemoryBlobStore(seq, ContractTestHarness.BlobWithDraws((5, "2026-04-25")));
        var state = new InMemoryStateStore(seq, new LoteriaLoaderState(5, "2026-04-25", clock.UtcNow, null));

        await ContractTestHarness.RunUseCaseAsync(api, blob, state, clock, delay);

        Assert.Equal(1, blob.Events.Count(e => e.StartsWith("Write:")));
        Assert.Equal(1, state.Events.Count(e => e.StartsWith("Write:")));
        ContractTestHarness.AssertBlobWrittenBeforeState(blob, state);
        ContractTestHarness.AssertTableBlobConsistent(blob, state);
    }
}
