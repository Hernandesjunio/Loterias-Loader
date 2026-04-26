using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Lotofacil.Loader.Application;
using Microsoft.Extensions.Options;

namespace Lotofacil.Loader.Infrastructure;

public sealed class LotodicasApiClient : ILotofacilApiClient
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

    public async Task<int> GetLatestContestIdAsync(CancellationToken ct)
    {
        using var doc = await SendJsonWithResilienceAsync(
            relativePath: $"/api/v2/lotofacil/results/last?token={Uri.EscapeDataString(_options.Token)}",
            ct
        );

        return doc.RootElement.GetProperty("data").GetProperty("draw_number").GetInt32();
    }

    public async Task<object> GetContestByIdRawAsync(int contestId, CancellationToken ct)
    {
        // Retorna JsonDocument (caller pode extrair/parsear).
        return await SendJsonWithResilienceAsync(
            relativePath: $"/api/v2/lotofacil/results/{contestId}?token={Uri.EscapeDataString(_options.Token)}",
            ct
        );
    }

    private async Task<JsonDocument> SendJsonWithResilienceAsync(string relativePath, CancellationToken ct)
    {
        // Contrato V0 (docs/spec-driven-execution-guide.md):
        // - timeout por tentativa = 10s
        // - 429 respeita Retry-After
        // - falhas transitórias: retry com espera fixa 30s quando não houver Retry-After
        const int maxAttempts = 2; // 1 tentativa + 1 retry (limite baixo para não estourar janela sem saber o deadline)

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

                // 4xx (exceto 429) e outros: falha dura para o caso de uso decidir (ele para com segurança).
                resp.EnsureSuccessStatusCode();

                var stream = await resp.Content.ReadAsStreamAsync(ct);
                return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested && attempt < maxAttempts)
            {
                // Timeout por tentativa (10s) - retry.
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

        // Alguns servidores enviam Retry-After como string raw.
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

