using System.Collections.Concurrent;
using System.Net;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace IntegrationTests.FunctionApp.V0;

internal sealed class LotodicasFakeServer : IAsyncDisposable
{
    private readonly string _token;
    private readonly ConcurrentQueue<RecordedCall> _calls = new();
    private readonly Dictionary<(string Modality, int Id), string> _byId = new();
    private readonly Dictionary<string, string> _lastJsonByModality = new();
    private readonly Dictionary<string, string> _allJsonByModality = new();
    private WebApplication? _app;

    public LotodicasFakeServer(string token) => _token = token;

    public Uri BaseUrl { get; private set; } = new Uri("http://127.0.0.1:0");

    public IReadOnlyList<RecordedCall> Calls => _calls.ToArray();

    public LotodicasFakeServer WithLatestResponseJson(string modality, string json)
    {
        _lastJsonByModality[modality] = json;
        return this;
    }

    public LotodicasFakeServer WithContestResponseJson(string modality, int id, string json)
    {
        _byId[(modality, id)] = json;
        return this;
    }

    public LotodicasFakeServer WithAllResponseJson(string modality, string json)
    {
        _allJsonByModality[modality] = json;
        return this;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        if (_app is not null)
        {
            return;
        }

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseKestrel(o => { o.ListenLocalhost(0); });

        var app = builder.Build();

        app.MapGet("/api/v2/{modality}/results/last", async (HttpContext context, string modality) =>
        {
            await HandleAsync(context, modality, endpoint: "last", contestId: null);
        });

        app.MapGet("/api/v2/{modality}/results/all", async (HttpContext context, string modality) =>
        {
            await HandleAsync(context, modality, endpoint: "all", contestId: null);
        });

        app.MapGet("/api/v2/{modality}/results/{id:int}", async (HttpContext context, string modality, int id) =>
        {
            await HandleAsync(context, modality, endpoint: "by_id", contestId: id);
        });

        _app = app;
        await app.StartAsync(ct);

        var addrs = app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()
            ?.Addresses;

        var first = addrs?.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(first))
        {
            throw new InvalidOperationException("Fake server did not expose listening address.");
        }

        BaseUrl = new Uri(first.TrimEnd('/') + "/");
    }

    private async Task HandleAsync(HttpContext ctx, string modality, string endpoint, int? contestId)
    {
        var token = ctx.Request.Query["token"].ToString();
        _calls.Enqueue(new RecordedCall(
            Method: ctx.Request.Method,
            Path: ctx.Request.Path.Value ?? "",
            QueryString: ctx.Request.QueryString.Value ?? "",
            Endpoint: endpoint,
            ContestId: contestId,
            Token: token,
            Modality: modality
        ));

        if (!string.Equals(token, _token, StringComparison.Ordinal))
        {
            ctx.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
            await ctx.Response.WriteAsync("{\"error\":\"invalid_token\"}");
            return;
        }

        string? payload = endpoint switch
        {
            "last" => _lastJsonByModality.GetValueOrDefault(modality),
            "all" => _allJsonByModality.GetValueOrDefault(modality),
            "by_id" when contestId is not null && _byId.TryGetValue((modality, contestId.Value), out var j) => j,
            _ => null
        };

        if (payload is null)
        {
            ctx.Response.StatusCode = (int)HttpStatusCode.NotFound;
            await ctx.Response.WriteAsync("{\"error\":\"fixture_not_found\"}");
            return;
        }

        ctx.Response.StatusCode = (int)HttpStatusCode.OK;
        ctx.Response.ContentType = "application/json; charset=utf-8";
        await ctx.Response.Body.WriteAsync(Encoding.UTF8.GetBytes(payload));
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is null)
        {
            return;
        }

        try
        {
            await _app.StopAsync();
        }
        finally
        {
            await _app.DisposeAsync();
            _app = null;
        }
    }

    internal sealed record RecordedCall(
        string Method,
        string Path,
        string QueryString,
        string Endpoint,
        int? ContestId,
        string Token,
        string Modality
    );
}
