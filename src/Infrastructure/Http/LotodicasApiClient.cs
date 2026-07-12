using System.Net;
using System.Text.Json;
using Lotofacil.Loader.Application;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace Lotofacil.Loader.Infrastructure;

public sealed class LotodicasApiClient : ILotteriesApiClient
{
    private readonly HttpClient _http;
    private readonly LotodicasOptions _options;
    private readonly ILogger<LotodicasApiClient> _log;
    private readonly IRunContext _runContext;
    private readonly IDelay _delay;

    public LotodicasApiClient(
        HttpClient http,
        IOptions<LotodicasOptions> options,
        ILogger<LotodicasApiClient> log,
        IRunContext runContext,
        IDelay delay)
    {
        _http = http;
        _options = options.Value;
        _log = log;
        _runContext = runContext;
        _delay = delay;
    }

    public Task<object> GetAllResultsRawAsync(string lotteryApiSegment, CancellationToken ct) =>
        GetAllResultsCoreAsync(lotteryApiSegment, ct);

    private async Task<object> GetAllResultsCoreAsync(string lotteryApiSegment, CancellationToken ct)
    {
        using var doc = await SendJsonWithResilienceAsync(
            relativePath: $"/api/v2/{lotteryApiSegment}/results/all?token={Uri.EscapeDataString(_options.Token)}",
            ct);
        return doc.RootElement.GetRawText();
    }

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
                _log.LogDebug("http.request.start attempt={attempt} max_attempts={max_attempts} timeout_seconds={timeout_seconds} path={path}",
                    attempt, maxAttempts, 10, SanitizePath(relativePath));
                Activity.Current?.AddEvent(new ActivityEvent(
                    "http.request.start",
                    tags: new ActivityTagsCollection
                    {
                        ["attempt"] = attempt,
                        ["max_attempts"] = maxAttempts,
                        ["timeout_seconds"] = 10,
                        ["path"] = SanitizePath(relativePath)
                    }));

                resp = await _http.GetAsync(relativePath, attemptCts.Token);

                _log.LogDebug("http.response status_code={status_code} attempt={attempt} path={path}",
                    (int)resp.StatusCode, attempt, SanitizePath(relativePath));
                Activity.Current?.AddEvent(new ActivityEvent(
                    "http.response",
                    tags: new ActivityTagsCollection
                    {
                        ["status_code"] = (int)resp.StatusCode,
                        ["attempt"] = attempt,
                        ["path"] = SanitizePath(relativePath)
                    }));

                if (resp.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                {
                    throw new LotodicasApiAuthException((int)resp.StatusCode, SanitizePath(relativePath));
                }

                if (resp.StatusCode == HttpStatusCode.TooManyRequests && attempt < maxAttempts)
                {
                    var delay = TryGetRetryAfter(resp) ?? TimeSpan.FromSeconds(30);
                    _runContext.IncrementRetries();
                    _log.LogDebug("http.retry_scheduled reason=429 retry_after_seconds={retry_after_seconds} attempt={attempt}", delay.TotalSeconds, attempt);
                    Activity.Current?.AddEvent(new ActivityEvent(
                        "http.retry_scheduled",
                        tags: new ActivityTagsCollection
                        {
                            ["reason"] = "429",
                            ["retry_after_seconds"] = delay.TotalSeconds,
                            ["attempt"] = attempt
                        }));
                    await DelayForRetryAsync(delay, "429", ct);
                    continue;
                }

                if ((int)resp.StatusCode >= 500 && attempt < maxAttempts)
                {
                    var delay = TimeSpan.FromSeconds(30);
                    _runContext.IncrementRetries();
                    _log.LogDebug("http.retry_scheduled reason=5xx retry_after_seconds={retry_after_seconds} attempt={attempt}", delay.TotalSeconds, attempt);
                    Activity.Current?.AddEvent(new ActivityEvent(
                        "http.retry_scheduled",
                        tags: new ActivityTagsCollection
                        {
                            ["reason"] = "5xx",
                            ["retry_after_seconds"] = delay.TotalSeconds,
                            ["attempt"] = attempt
                        }));
                    await DelayForRetryAsync(delay, "5xx", ct);
                    continue;
                }

                resp.EnsureSuccessStatusCode();

                var stream = await resp.Content.ReadAsStreamAsync(ct);
                return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            }
            catch (BudgetExceededException)
            {
                throw;
            }
            catch (LotodicasApiAuthException)
            {
                throw;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested && attempt < maxAttempts)
            {
                var delay = TimeSpan.FromSeconds(30);
                _runContext.IncrementRetries();
                _log.LogDebug("http.retry_scheduled reason=timeout retry_after_seconds={retry_after_seconds} attempt={attempt}", delay.TotalSeconds, attempt);
                Activity.Current?.AddEvent(new ActivityEvent(
                    "http.retry_scheduled",
                    tags: new ActivityTagsCollection
                    {
                        ["reason"] = "timeout",
                        ["retry_after_seconds"] = delay.TotalSeconds,
                        ["attempt"] = attempt
                    }));
                await DelayForRetryAsync(delay, "timeout", ct);
                continue;
            }
            finally
            {
                resp?.Dispose();
            }
        }

        throw new InvalidOperationException("HTTP resilience loop exhausted unexpectedly.");
    }

    private async Task DelayForRetryAsync(TimeSpan requestedDelay, string reason, CancellationToken ct)
    {
        var budget = _runContext.CurrentBudget;
        var cappedDelay = budget is null ? requestedDelay : budget.CapWait(requestedDelay);

        if (cappedDelay <= TimeSpan.Zero)
        {
            _log.LogWarning(
                "http.retry_skipped reason=budget retry_reason={retry_reason} requested_seconds={requested_seconds} remaining_seconds={remaining_seconds}",
                reason,
                requestedDelay.TotalSeconds,
                budget?.Remaining.TotalSeconds ?? 0d);
            Activity.Current?.AddEvent(new ActivityEvent(
                "http.retry_skipped",
                tags: new ActivityTagsCollection
                {
                    ["reason"] = "budget",
                    ["retry_reason"] = reason,
                    ["requested_seconds"] = requestedDelay.TotalSeconds
                }));
            throw new BudgetExceededException($"Retry wait for {reason} exceeds remaining execution budget.");
        }

        if (cappedDelay < requestedDelay)
        {
            _log.LogDebug(
                "http.retry_capped retry_reason={retry_reason} requested_seconds={requested_seconds} capped_seconds={capped_seconds}",
                reason,
                requestedDelay.TotalSeconds,
                cappedDelay.TotalSeconds);
        }

        _runContext.AddWaitSeconds(cappedDelay.TotalSeconds);
        await _delay.DelayAsync(cappedDelay, ct);
    }

    private static string SanitizePath(string relativePath)
    {
        var i = relativePath.IndexOf("token=", StringComparison.OrdinalIgnoreCase);
        if (i < 0)
        {
            return relativePath;
        }

        var start = i + "token=".Length;
        var end = relativePath.IndexOf('&', start);
        return end < 0
            ? relativePath[..start] + "***"
            : relativePath[..start] + "***" + relativePath[end..];
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
