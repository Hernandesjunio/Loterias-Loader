using Lotofacil.Loader.Application;
using Lotofacil.Loader.Domain;
using Lotofacil.Loader.V0.Contract;
using Xunit;

namespace ContractTests.V0;

public sealed class V0ContractBehaviorTests
{
    [Fact]
    public async Task J1_every_run_calls_only_results_all()
    {
        var clock = new FakeClock(ContractTestHarness.Utc("2026-04-27T23:30:00Z"));
        var api = new FakeApi()
            .WithAllResults(ContractTestHarness.AllResultsJson(
                ContractTestHarness.ContestItemJson(id: 1, date: "2026-04-20"),
                ContractTestHarness.ContestItemJson(id: 2, date: "2026-04-21", winners15: 3)));

        var seq = new EventSequencer();
        var blob = new InMemoryBlobStore(seq, ContractTestHarness.BlobWithDraws((10, "2026-04-25")));
        var state = new InMemoryStateStore(seq, existing: new LoteriaLoaderState(10, "2026-04-25", clock.UtcNow, null));

        await EntryPoint.RunAsync(api, blob, state, clock, new FakeDelay(clock), CancellationToken.None);

        Assert.Equal(new[] { "GetAll:lotofacil" }, api.Calls);
    }

    [Fact]
    public async Task J2_empty_state_syncs_from_results_all_and_persists_blob_before_state()
    {
        var clock = new FakeClock(ContractTestHarness.Utc("2026-04-27T23:30:00Z"));
        var api = new FakeApi()
            .WithAllResults(ContractTestHarness.AllResultsJson(
                ContractTestHarness.ContestItemJson(id: 1, date: "2026-04-20", winners15: 0),
                ContractTestHarness.ContestItemJson(id: 2, date: "2026-04-21", winners15: 3)));

        var seq = new EventSequencer();
        var blob = InMemoryBlobStore.WithoutExistingBlob(seq);
        var state = new InMemoryStateStore(seq, existing: new LoteriaLoaderState(0, null, clock.UtcNow, null));

        await EntryPoint.RunAsync(api, blob, state, clock, new FakeDelay(clock), CancellationToken.None);

        Assert.Equal(new[] { "GetAll:lotofacil" }, api.Calls);
        Assert.Equal(new[] { 1, 2 }, blob.Current.Draws.Select(d => d.ContestId).ToArray());
        Assert.Equal(2, state.Current!.LastLoadedContestId);
        Assert.Equal("2026-04-21", state.Current.LastLoadedDrawDate);
        Assert.True(
            blob.SequenceIdOfLastWrite < state.SequenceIdOfLastWrite,
            "Contrato V0: persistir blob antes do Table state.");
    }

    [Fact]
    public async Task J3_idempotent_rerun_still_calls_api_and_rewrites_storage()
    {
        var clock = new FakeClock(ContractTestHarness.Utc("2026-04-27T23:30:00Z"));
        var allResults = ContractTestHarness.AllResultsJson(
            ContractTestHarness.ContestItemJson(id: 1, date: "2026-04-20"),
            ContractTestHarness.ContestItemJson(id: 2, date: "2026-04-21", winners15: 3));
        var api = new FakeApi().WithAllResults(allResults);

        var seq = new EventSequencer();
        var blob = InMemoryBlobStore.WithoutExistingBlob(seq);
        var state = new InMemoryStateStore(seq, existing: new LoteriaLoaderState(0, null, clock.UtcNow, null));

        await EntryPoint.RunAsync(api, blob, state, clock, new FakeDelay(clock), CancellationToken.None);
        var writesAfterFirstRun = blob.Events.Count + state.Events.Count;

        clock.SetUtcNow(clock.UtcNow.AddMinutes(10));
        await EntryPoint.RunAsync(api, blob, state, clock, new FakeDelay(clock), CancellationToken.None);

        Assert.Equal(2, api.Calls.Count);
        Assert.Equal(2, api.Calls.Count(c => c == "GetAll:lotofacil"));
        Assert.True(writesAfterFirstRun < blob.Events.Count + state.Events.Count);
        Assert.Equal(new[] { 1, 2 }, blob.Current.Draws.Select(d => d.ContestId).ToArray());
    }

    [Fact]
    public async Task Sync_replaces_existing_blob_with_full_catalog_from_api()
    {
        var clock = new FakeClock(ContractTestHarness.Utc("2026-04-27T23:30:00Z"));
        var api = new FakeApi()
            .WithAllResults(ContractTestHarness.AllResultsJson(
                ContractTestHarness.ContestItemJson(id: 1, date: "2026-04-01"),
                ContractTestHarness.ContestItemJson(id: 2, date: "2026-04-02"),
                ContractTestHarness.ContestItemJson(id: 3, date: "2026-04-03", winners15: 1)));

        var seq = new EventSequencer();
        var blob = new InMemoryBlobStore(seq, ContractTestHarness.BlobWithDraws((5, "2026-04-25")));
        var state = new InMemoryStateStore(seq, existing: new LoteriaLoaderState(5, "2026-04-25", clock.UtcNow, null));

        await EntryPoint.RunAsync(api, blob, state, clock, new FakeDelay(clock), CancellationToken.None);

        Assert.Equal(new[] { "GetAll:lotofacil" }, api.Calls);
        Assert.Equal(new[] { 1, 2, 3 }, blob.Current.Draws.Select(d => d.ContestId).ToArray());
        Assert.Equal(3, state.Current!.LastLoadedContestId);
    }

    [Fact]
    public async Task When_gap_in_contest_ids_persisted_checkpoint_stops_at_contiguous_prefix()
    {
        var clock = new FakeClock(ContractTestHarness.Utc("2026-04-27T23:30:00Z"));
        var api = new FakeApi()
            .WithAllResults(ContractTestHarness.AllResultsJson(
                ContractTestHarness.ContestItemJson(id: 1, date: "2026-04-01"),
                ContractTestHarness.ContestItemJson(id: 3, date: "2026-04-03")));

        var seq = new EventSequencer();
        var blob = InMemoryBlobStore.WithoutExistingBlob(seq);
        var state = new InMemoryStateStore(seq, existing: new LoteriaLoaderState(0, null, clock.UtcNow, null));

        var outcome = await ContractTestHarness.RunUseCaseAsync(api, blob, state, clock);

        Assert.Equal(ReasonStop.COMPLETED_SUCCESS, outcome.ReasonStop);
        Assert.Equal(1, state.Current!.LastLoadedContestId);
        Assert.Equal(2, outcome.ProcessedCount);
    }
}
