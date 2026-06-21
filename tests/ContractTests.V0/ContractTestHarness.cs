using System.Text.Json;
using Lotofacil.Loader.Application;
using Lotofacil.Loader.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ContractTests.V0;

internal static class ContractTestHarness
{
    internal static async Task<UpdateLoteriaResultsOutcome> RunUseCaseAsync(
        ILotteriesApiClient api,
        ILoteriaBlobStore blob,
        ILoteriaStateStore state,
        IClock clock,
        IDelay delay,
        CancellationToken ct = default)
    {
        var services = new ServiceCollection();
        services.AddSingleton(clock);
        services.AddSingleton(delay);
        services.AddSingleton(api);
        services.AddSingleton(blob);
        services.AddSingleton(state);
        services.AddSingleton<ILoteriaBlobCatalog, LotofacilBlobCatalog>();
        services.AddSingleton<IRunContext, AsyncLocalRunContext>();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton(sp => new LoteriaResultsUpdateUseCase(
            sp.GetRequiredService<ILogger<LoteriaResultsUpdateUseCase>>(),
            sp.GetRequiredService<IRunContext>(),
            sp.GetRequiredService<IClock>(),
            sp.GetRequiredService<IDelay>(),
            sp.GetRequiredService<ILotteriesApiClient>(),
            sp.GetRequiredService<ILoteriaBlobStore>(),
            sp.GetRequiredService<ILoteriaStateStore>(),
            sp.GetRequiredService<ILoteriaBlobCatalog>(),
            modalityKey: LoteriaModalityKeys.Lotofacil,
            lotteryApiSegment: LoteriaModalityKeys.Lotofacil));

        await using var sp = services.BuildServiceProvider(validateScopes: true);
        using var runScope = sp.GetRequiredService<IRunContext>().BeginRun(Guid.NewGuid().ToString("n"), LoteriaModalityKeys.Lotofacil);
        return await sp.GetRequiredService<LoteriaResultsUpdateUseCase>().ExecuteAsync(ct);
    }

    internal static void AssertTableBlobConsistent(InMemoryBlobStore blob, InMemoryStateStore state)
    {
        Assert.NotNull(state.Current);

        if (blob.Exists && blob.Current.Draws.Count > 0)
        {
            var blobMaxId = blob.Current.Draws.Max(d => d.ContestId);

            Assert.True(
                state.Current!.LastLoadedContestId <= blobMaxId,
                $"Table ({state.Current.LastLoadedContestId}) não pode estar à frente do blob ({blobMaxId}).");

            if (state.Current.LastLoadedContestId > 0)
            {
                var stateDraw = blob.Current.Draws.Single(d => d.ContestId == state.Current.LastLoadedContestId);
                Assert.Equal(state.Current.LastLoadedDrawDate, stateDraw.DrawDate);
            }
        }
        else if (state.Current!.LastLoadedContestId > 0)
        {
            Assert.Fail("Table indica concursos carregados, mas o blob está ausente ou vazio.");
        }
    }

    internal static void AssertBlobWrittenBeforeState(InMemoryBlobStore blob, InMemoryStateStore state)
    {
        if (blob.SequenceIdOfLastWrite < 0 || state.SequenceIdOfLastWrite < 0)
        {
            return;
        }

        Assert.True(
            blob.SequenceIdOfLastWrite < state.SequenceIdOfLastWrite,
            "Contrato V0: persistir blob antes do Table state.");
    }

    internal static DateTimeOffset Utc(string iso8601Utc) =>
        DateTimeOffset.Parse(iso8601Utc, null, System.Globalization.DateTimeStyles.AssumeUniversal);

    internal static LotofacilBlobDocument BlobWithDraws(params (int Id, string Date)[] draws) =>
        new(draws.Select(d => new LotofacilBlobDraw(
            ContestId: d.Id,
            DrawDate: d.Date,
            Numbers: new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15 },
            Winners15: 0,
            HasWinner15: false)).ToArray());

    internal static string ContestJson(int id, string date, int winners15 = 0)
    {
        var obj = new
        {
            data = new
            {
                draw_number = id,
                draw_date = date,
                drawing = new { draw = new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15 } },
                prizes = new[]
                {
                    new { name = "15 acertos", winners = winners15 }
                }
            }
        };

        return JsonSerializer.Serialize(obj);
    }

    internal static string ContestItemJson(int id, string date, int winners15 = 0)
    {
        var obj = new
        {
            draw_number = id,
            draw_date = date,
            drawing = new { draw = new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15 } },
            prizes = new[]
            {
                new { name = "15 acertos", winners = winners15 }
            }
        };

        return JsonSerializer.Serialize(obj);
    }

    internal static string AllResultsJson(params string[] contestItemsJson)
    {
        var items = contestItemsJson.Select(static item => JsonSerializer.Deserialize<JsonElement>(item)).ToArray();
        return JsonSerializer.Serialize(new { data = items });
    }
}

internal sealed class FakeClock : IClock
{
    public FakeClock(DateTimeOffset utcNow) => UtcNow = utcNow;
    public DateTimeOffset UtcNow { get; private set; }
    public void SetUtcNow(DateTimeOffset utcNow) => UtcNow = utcNow;
}

internal sealed class FakeDelay : IDelay
{
    private readonly FakeClock _clock;
    public FakeDelay(FakeClock clock) => _clock = clock;
    public List<TimeSpan> Delays { get; } = new();
    public Task DelayAsync(TimeSpan delay, CancellationToken ct)
    {
        Delays.Add(delay);
        _clock.SetUtcNow(_clock.UtcNow.Add(delay));
        return Task.CompletedTask;
    }
}

internal sealed class FakeApi : ILotteriesApiClient
{
    private readonly Dictionary<int, string> _byId = new();
    private string? _allResultsRaw;
    private int _latestId;

    public FakeApi(int latestId) => _latestId = latestId;

    public List<string> Calls { get; } = new();

    public FakeApi SetLatest(int latestId)
    {
        _latestId = latestId;
        return this;
    }

    public FakeApi WithContest(int id, string rawJson)
    {
        _byId[id] = rawJson;
        return this;
    }

    public FakeApi WithAllResults(string rawJson)
    {
        _allResultsRaw = rawJson;
        return this;
    }

    public Task<int> GetLatestContestIdAsync(string lotteryApiSegment, CancellationToken ct)
    {
        Calls.Add($"GetLatest:{lotteryApiSegment}");
        return Task.FromResult(_latestId);
    }

    public Task<object> GetContestByIdRawAsync(string lotteryApiSegment, int contestId, CancellationToken ct)
    {
        Calls.Add($"GetById:{lotteryApiSegment}:{contestId}");
        if (!_byId.TryGetValue(contestId, out var raw))
        {
            throw new InvalidOperationException($"Missing fixture for contestId={contestId}");
        }

        return Task.FromResult<object>(raw);
    }

    public Task<object> GetAllResultsRawAsync(string lotteryApiSegment, CancellationToken ct)
    {
        Calls.Add($"GetAll:{lotteryApiSegment}");
        if (string.IsNullOrWhiteSpace(_allResultsRaw))
        {
            throw new InvalidOperationException("Missing fixture for /results/all");
        }

        return Task.FromResult<object>(_allResultsRaw);
    }
}

internal sealed class InMemoryBlobStore : ILoteriaBlobStore
{
    private readonly EventSequencer _seq;

    public InMemoryBlobStore(EventSequencer seq, LotofacilBlobDocument? existing = null)
    {
        _seq = seq;
        Exists = true;
        Current = existing ?? new LotofacilBlobDocument(Array.Empty<LotofacilBlobDraw>());
    }

    private InMemoryBlobStore(EventSequencer seq, bool exists, LotofacilBlobDocument current)
    {
        _seq = seq;
        Exists = exists;
        Current = current;
    }

    public static InMemoryBlobStore WithoutExistingBlob(EventSequencer seq) =>
        new(seq, exists: false, current: new LotofacilBlobDocument(Array.Empty<LotofacilBlobDraw>()));

    public bool Exists { get; private set; }
    public LotofacilBlobDocument Current { get; private set; }
    public List<string> Events { get; } = new();
    public int SequenceIdOfLastWrite { get; private set; } = -1;

    public Task<object?> TryReadRawAsync(CancellationToken ct) =>
        Task.FromResult<object?>(Exists ? Current : null);

    public Task WriteRawAsync(object document, CancellationToken ct)
    {
        Current = document switch
        {
            LotofacilBlobDocument d => d,
            string s => JsonSerializer.Deserialize<LotofacilBlobDocument>(s) ??
                        new LotofacilBlobDocument(Array.Empty<LotofacilBlobDraw>()),
            _ => throw new InvalidOperationException($"Unsupported blob document type: {document.GetType().FullName}")
        };
        Exists = true;

        SequenceIdOfLastWrite = _seq.Next();
        Events.Add($"Write:{SequenceIdOfLastWrite}");
        return Task.CompletedTask;
    }
}

internal sealed class InMemoryStateStore : ILoteriaStateStore
{
    private readonly EventSequencer _seq;

    public InMemoryStateStore(EventSequencer seq, LoteriaLoaderState? existing = null)
    {
        _seq = seq;
        Current = existing;
    }

    public LoteriaLoaderState? Current { get; private set; }
    public List<string> Events { get; } = new();
    public int SequenceIdOfLastWrite { get; private set; } = -1;

    public Task<object?> TryReadRawAsync(CancellationToken ct) => Task.FromResult<object?>(Current);

    public Task WriteRawAsync(object state, CancellationToken ct)
    {
        Current = state switch
        {
            LoteriaLoaderState s => s,
            string raw => JsonSerializer.Deserialize<LoteriaLoaderState>(raw),
            _ => throw new InvalidOperationException($"Unsupported state type: {state.GetType().FullName}")
        };

        SequenceIdOfLastWrite = _seq.Next();
        Events.Add($"Write:{SequenceIdOfLastWrite}");
        return Task.CompletedTask;
    }
}

internal sealed class EventSequencer
{
    private int _n;
    public int Next() => ++_n;
}
