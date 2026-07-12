using System.Net;
using System.Text;
using Lotofacil.Loader.Application;
using Lotofacil.Loader.Domain;
using Lotofacil.Loader.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace ContractTests.V0;

public sealed class LotodicasApiClientBulkParseTests
{
    [Fact]
    public async Task K1_real_http_client_bulk_response_parses_after_document_disposed()
    {
        var payload = ContractTestHarness.AllResultsJson(
            ContractTestHarness.ContestItemJson(id: 1, date: "2026-04-20"),
            ContractTestHarness.ContestItemJson(id: 2, date: "2026-04-21", winners15: 3));

        var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.invalid") };

        var clock = new FakeClock(ContractTestHarness.Utc("2026-07-12T22:14:00Z"));
        var delay = new FakeDelay(clock);
        var runContext = new AsyncLocalRunContext();
        using var runScope = runContext.BeginRun("k1-run", LoteriaModalityKeys.Lotofacil);
        runContext.SetExecutionBudget(new ExecutionBudget(clock, clock.UtcNow.AddMinutes(3)));

        ILotteriesApiClient api = new LotodicasApiClient(
            http,
            Options.Create(new LotodicasOptions { BaseUrl = "https://example.invalid", Token = "token" }),
            NullLogger<LotodicasApiClient>.Instance,
            runContext,
            delay);

        var raw = await api.GetAllResultsRawAsync(LoteriaModalityKeys.Lotofacil, CancellationToken.None);
        Assert.IsType<string>(raw);

        var seq = new EventSequencer();
        var blob = InMemoryBlobStore.WithoutExistingBlob(seq);
        var state = new InMemoryStateStore(seq, new LoteriaLoaderState(0, null, clock.UtcNow, null));

        var outcome = await ContractTestHarness.RunUseCaseAsync(api, blob, state, clock);

        Assert.Equal(ReasonStop.COMPLETED_SUCCESS, outcome.ReasonStop);
        Assert.Equal(new[] { 1, 2 }, blob.Current.Draws.Select(d => d.ContestId).ToArray());
        Assert.Equal(2, state.Current!.LastLoadedContestId);
        Assert.Equal(2, handler.CallCount);
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<int, HttpResponseMessage> _factory;

        public StubHttpMessageHandler(Func<int, HttpResponseMessage> factory) => _factory = factory;

        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(_factory(CallCount));
        }
    }
}
