using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Lotofacil.Loader.Application;
using Microsoft.Extensions.Options;

namespace Lotofacil.Loader.Infrastructure;

public sealed class LotodicasApiClient : ILotteriesApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _http;
    private readonly LotodicasOptions _options;

    public LotodicasApiClient(HttpClient http, IOptions<LotodicasOptions> options)
    {
        _http = http;
        _options = options.Value;
    }

    public Task<int> GetLatestContestIdAsync(string lotteryApiSegment, CancellationToken ct) =>
        GetLatestContestIdCoreAsync(lotteryApiSegment, ct);

    public Task<object> GetContestByIdRawAsync(string lotteryApiSegment, int contestId, CancellationToken ct) =>
        GetContestByIdCoreAsync(lotteryApiSegment, contestId, ct);

    public Task<object> GetAllResultsRawAsync(string lotteryApiSegment, CancellationToken ct) =>
        GetAllResultsCoreAsync(lotteryApiSegment, ct);

    private async Task<int> GetLatestContestIdCoreAsync(string lotteryApiSegment, CancellationToken ct)
    {
        using var doc = await SendJsonWithResilienceAsync(
            relativePath: $"/api/v2/{lotteryApiSegment}/results/last?token={Uri.EscapeDataString(_options.Token)}",
            ct
        );

        return doc.RootElement.GetProperty("data").GetProperty("draw_number").GetInt32();
    }

    private async Task<object> GetContestByIdCoreAsync(string lotteryApiSegment, int contestId, CancellationToken ct) =>
        await SendJsonWithResilienceAsync(
            relativePath: $"/api/v2/{lotteryApiSegment}/results/{contestId}?token={Uri.EscapeDataString(_options.Token)}",
            ct
        );

    private async Task<object> GetAllResultsCoreAsync(string lotteryApiSegment, CancellationToken ct) =>
        await SendJsonWithResilienceAsync(
            relativePath: $"/api/v2/{lotteryApiSegment}/results/all?token={Uri.EscapeDataString(_options.Token)}",
            ct
        );

    private async Task<JsonDocument> SendJsonWithResilienceAsync(string relativePath, CancellationToken ct)
    {
        const int maxAttempts = 2;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            attemptCts.CancelAfter(TimeSpan.FromSeconds(10));

            HttpResponseMessage? resp = null;
            try
            {
                resp = await _http.GetAsync(relativePath, attemptCts.Token);

                if (resp.StatusCode == HttpStatusCode.TooManyRequests && attempt < maxAttempts)
                {
                    var delay = TryGetRetryAfter(resp) ?? TimeSpan.FromSeconds(30);
                    await Task.Delay(delay, ct);
                    continue;
                }

                if ((int)resp.StatusCode >= 500 && attempt < maxAttempts)
                {
                    await Task.Delay(TimeSpan.FromSeconds(30), ct);
                    continue;
                }

                resp.EnsureSuccessStatusCode();

                var stream = await resp.Content.ReadAsStreamAsync(ct);
                return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested && attempt < maxAttempts)
            {
                await Task.Delay(TimeSpan.FromSeconds(30), ct);
                continue;
            }
            finally
            {
                resp?.Dispose();
            }
        }

        throw new InvalidOperationException("HTTP resilience loop exhausted unexpectedly.");
    }

    private static TimeSpan? TryGetRetryAfter(HttpResponseMessage resp)
    {
        if (resp.Headers.RetryAfter is { } ra)
        {
            if (ra.Delta is { } d)
            {
                return d;
            }

            if (ra.Date is { } dt)
            {
                var delta = dt - DateTimeOffset.UtcNow;
                return delta > TimeSpan.Zero ? delta : TimeSpan.Zero;
            }
        }

        if (resp.Headers.TryGetValues("Retry-After", out var values))
        {
            var v = values.FirstOrDefault();
            if (int.TryParse(v, out var seconds) && seconds >= 0)
            {
                return TimeSpan.FromSeconds(seconds);
            }
        }

        return null;
    }
}
