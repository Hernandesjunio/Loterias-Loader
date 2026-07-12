using Lotofacil.Loader.Application;
using Lotofacil.Loader.Domain;
using Xunit;

namespace ContractTests.V0;

public sealed class BulkSyncContractTests
{
    [Fact]
    public async Task J5_use_case_maps_401_to_HARD_FAIL_API_AUTH()
    {
        var clock = new FakeClock(ContractTestHarness.Utc("2026-04-27T23:30:00Z"));
        var api = new AuthFailingApi();
        var seq = new EventSequencer();
        var blob = InMemoryBlobStore.WithoutExistingBlob(seq);
        var state = new InMemoryStateStore(seq, new LoteriaLoaderState(0, null, clock.UtcNow, null));

        var outcome = await ContractTestHarness.RunUseCaseAsync(api, blob, state, clock);

        Assert.Equal(ReasonStop.HARD_FAIL_API_AUTH, outcome.ReasonStop);
        Assert.Empty(blob.Events);
        Assert.Empty(state.Events);
    }

    private sealed class AuthFailingApi : ILotteriesApiClient
    {
        public Task<object> GetAllResultsRawAsync(string lotteryApiSegment, CancellationToken ct) =>
            throw new LotodicasApiAuthException(401, "/api/v2/lotofacil/results/all");
    }
}
