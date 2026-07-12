using Lotofacil.Loader.Application;
using Lotofacil.Loader.Domain;
using Xunit;

namespace ContractTests.V0;

public sealed class TableBlobConsistencyTests
{
    [Fact]
    public async Task After_sync_persist_table_matches_blob_checkpoint()
    {
        var clock = new FakeClock(ContractTestHarness.Utc("2026-04-27T23:30:00Z"));
        var api = new FakeApi()
            .WithAllResults(ContractTestHarness.AllResultsJson(
                ContractTestHarness.ContestItemJson(id: 1, date: "2026-04-25"),
                ContractTestHarness.ContestItemJson(id: 2, date: "2026-04-26"),
                ContractTestHarness.ContestItemJson(id: 3, date: "2026-04-27", winners15: 5)));

        var seq = new EventSequencer();
        var blob = new InMemoryBlobStore(seq, ContractTestHarness.BlobWithDraws((5, "2026-04-25")));
        var state = new InMemoryStateStore(seq, new LoteriaLoaderState(5, "2026-04-25", clock.UtcNow, null));

        var outcome = await ContractTestHarness.RunUseCaseAsync(api, blob, state, clock);

        Assert.Equal(ReasonStop.COMPLETED_SUCCESS, outcome.ReasonStop);
        ContractTestHarness.AssertTableBlobConsistent(blob, state);
        ContractTestHarness.AssertBlobWrittenBeforeState(blob, state);
        Assert.Equal(3, state.Current!.LastLoadedContestId);
        Assert.Equal("2026-04-27", state.Current.LastLoadedDrawDate);
    }

    [Fact]
    public async Task After_initial_sync_table_matches_blob_checkpoint()
    {
        var clock = new FakeClock(ContractTestHarness.Utc("2026-04-27T23:30:00Z"));
        var api = new FakeApi()
            .WithAllResults(ContractTestHarness.AllResultsJson(
                ContractTestHarness.ContestItemJson(id: 1, date: "2026-04-20"),
                ContractTestHarness.ContestItemJson(id: 2, date: "2026-04-21", winners15: 3)));

        var seq = new EventSequencer();
        var blob = InMemoryBlobStore.WithoutExistingBlob(seq);
        var state = new InMemoryStateStore(seq, new LoteriaLoaderState(0, null, clock.UtcNow, null));

        var outcome = await ContractTestHarness.RunUseCaseAsync(api, blob, state, clock);

        Assert.Equal(ReasonStop.COMPLETED_SUCCESS, outcome.ReasonStop);
        ContractTestHarness.AssertTableBlobConsistent(blob, state);
        ContractTestHarness.AssertBlobWrittenBeforeState(blob, state);
    }

    [Fact]
    public async Task When_state_missing_initialized_from_blob_table_matches_blob_without_blob_write()
    {
        var clock = new FakeClock(ContractTestHarness.Utc("2026-04-27T23:30:00Z"));
        var api = new FakeApi()
            .WithAllResults(ContractTestHarness.AllResultsJson(
                ContractTestHarness.ContestItemJson(id: 1, date: "2026-04-01"),
                ContractTestHarness.ContestItemJson(id: 2, date: "2026-04-02"),
                ContractTestHarness.ContestItemJson(id: 3, date: "2026-04-03"),
                ContractTestHarness.ContestItemJson(id: 4, date: "2026-04-04"),
                ContractTestHarness.ContestItemJson(id: 5, date: "2026-04-25")));

        var seq = new EventSequencer();
        var blob = new InMemoryBlobStore(seq, ContractTestHarness.BlobWithDraws((5, "2026-04-25")));
        var state = new InMemoryStateStore(seq, existing: null);

        var outcome = await ContractTestHarness.RunUseCaseAsync(api, blob, state, clock);

        Assert.Equal(ReasonStop.COMPLETED_SUCCESS, outcome.ReasonStop);
        Assert.Equal(2, state.Events.Count(e => e.StartsWith("Write:")));
        ContractTestHarness.AssertTableBlobConsistent(blob, state);
        Assert.Equal(5, state.Current!.LastLoadedContestId);
        Assert.Equal("2026-04-25", state.Current.LastLoadedDrawDate);
    }

    [Fact]
    public async Task When_table_ahead_of_blob_hard_fails_without_api_or_persist()
    {
        var clock = new FakeClock(ContractTestHarness.Utc("2026-06-21T23:30:00Z"));
        var api = new FakeApi()
            .WithAllResults(ContractTestHarness.AllResultsJson(
                ContractTestHarness.ContestItemJson(id: 1, date: "2026-05-01")));

        var seq = new EventSequencer();
        var blob = new InMemoryBlobStore(seq, ContractTestHarness.BlobWithDraws((100, "2026-05-01")));
        var state = new InMemoryStateStore(
            seq,
            new LoteriaLoaderState(150, "2026-06-21", clock.UtcNow, null));

        var outcome = await ContractTestHarness.RunUseCaseAsync(api, blob, state, clock);

        Assert.Equal(ReasonStop.HARD_FAIL_STATE_INCONSISTENT_TABLE_GT_BLOB, outcome.ReasonStop);
        Assert.Empty(api.Calls);
        Assert.Empty(blob.Events);
        Assert.Empty(state.Events);
    }

    [Fact]
    public async Task Persist_writes_never_update_table_before_blob_in_same_run()
    {
        var clock = new FakeClock(ContractTestHarness.Utc("2026-04-27T23:30:00Z"));
        var api = new FakeApi()
            .WithAllResults(ContractTestHarness.AllResultsJson(
                ContractTestHarness.ContestItemJson(id: 1, date: "2026-04-25"),
                ContractTestHarness.ContestItemJson(id: 2, date: "2026-04-26"),
                ContractTestHarness.ContestItemJson(id: 3, date: "2026-04-27", winners15: 1)));

        var seq = new EventSequencer();
        var blob = new InMemoryBlobStore(seq, ContractTestHarness.BlobWithDraws((5, "2026-04-25")));
        var state = new InMemoryStateStore(seq, new LoteriaLoaderState(5, "2026-04-25", clock.UtcNow, null));

        await ContractTestHarness.RunUseCaseAsync(api, blob, state, clock);

        Assert.Equal(1, blob.Events.Count(e => e.StartsWith("Write:")));
        Assert.Equal(1, state.Events.Count(e => e.StartsWith("Write:")));
        ContractTestHarness.AssertBlobWrittenBeforeState(blob, state);
        ContractTestHarness.AssertTableBlobConsistent(blob, state);
    }
}
