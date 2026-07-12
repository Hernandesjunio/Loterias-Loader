using System.Net;
using Lotofacil.Loader.Application;
using Lotofacil.Loader.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace ContractTests.V0;

public sealed class LotodicasApiClientBudgetTests
{
    [Fact]
    public async Task J4_retry_after_429_is_capped_to_remaining_budget()
    {
        var t0 = ContractTestHarness.Utc("2026-07-12T20:00:00Z");
        var clock = new FakeClock(t0);
        var delay = new FakeDelay(clock);
        var runContext = new AsyncLocalRunContext();
        using var _ = runContext.BeginRun("test-run", LoteriaModalityKeys.Lotofacil);
        runContext.SetExecutionBudget(new ExecutionBudget(clock, t0.AddSeconds(20)));

        var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                Headers = { RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(60)) }
            });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.invalid") };

        var client = CreateClient(http, runContext, delay);

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.GetAllResultsRawAsync(LoteriaModalityKeys.Lotofacil, CancellationToken.None));

        Assert.Equal(new[] { TimeSpan.FromSeconds(20) }, delay.Delays);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task J4_retry_skipped_immediately_when_budget_is_zero()
    {
        var t0 = ContractTestHarness.Utc("2026-07-12T20:00:00Z");
        var clock = new FakeClock(t0);
        var delay = new FakeDelay(clock);
        var runContext = new AsyncLocalRunContext();
        using var _ = runContext.BeginRun("test-run", LoteriaModalityKeys.Lotofacil);
        runContext.SetExecutionBudget(new ExecutionBudget(clock, t0));

        var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                Headers = { RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(60)) }
            });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.invalid") };

        var client = CreateClient(http, runContext, delay);

        await Assert.ThrowsAsync<BudgetExceededException>(() =>
            client.GetAllResultsRawAsync(LoteriaModalityKeys.Lotofacil, CancellationToken.None));

        Assert.Empty(delay.Delays);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task J5_auth_error_does_not_retry()
    {
        var clock = new FakeClock(ContractTestHarness.Utc("2026-07-12T20:00:00Z"));
        var delay = new FakeDelay(clock);
        var runContext = new AsyncLocalRunContext();
        using var _ = runContext.BeginRun("test-run", LoteriaModalityKeys.Lotofacil);

        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.invalid") };

        var client = CreateClient(http, runContext, delay);

        await Assert.ThrowsAsync<LotodicasApiAuthException>(() =>
            client.GetAllResultsRawAsync(LoteriaModalityKeys.Lotofacil, CancellationToken.None));

        Assert.Equal(1, handler.CallCount);
    }

    private static LotodicasApiClient CreateClient(
        HttpClient http,
        IRunContext runContext,
        IDelay delay) =>
        new(
            http,
            Options.Create(new LotodicasOptions { BaseUrl = "https://example.invalid", Token = "token" }),
            NullLogger<LotodicasApiClient>.Instance,
            runContext,
            delay);

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
