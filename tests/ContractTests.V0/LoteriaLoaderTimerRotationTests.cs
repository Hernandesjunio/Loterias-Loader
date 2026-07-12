using Lotofacil.Loader.Application;
using Lotofacil.Loader.Composition;
using Lotofacil.Loader.Domain;
using Lotofacil.Loader.FunctionApp;
using Lotofacil.Loader.FunctionApp.Functions;
using Lotofacil.Loader.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace ContractTests.V0;

public sealed class LoteriaLoaderTimerRotationTests
{
    [Fact]
    public async Task T1_rotation_mode_executes_only_one_modality_per_invocation()
    {
        var tracking = new ModalityTrackingApiClient();
        var fn = CreateFunction(tracking, sequentialAllModalities: false);

        await fn.RunAsync(timer: null!, CancellationToken.None);

        Assert.Single(tracking.ModalitiesCalled);
    }

    [Fact]
    public async Task T2_rotation_mode_executes_scheduler_selected_modality()
    {
        var store = new InMemoryLoteriasLoaderSchedulerStore();
        store.Seed(new LoteriasLoaderSchedulerState(1, LoteriaModalityKeys.Lotofacil, null, null));

        var tracking = new ModalityTrackingApiClient();
        var fn = CreateFunction(tracking, sequentialAllModalities: false, schedulerStore: store);

        await fn.RunAsync(timer: null!, CancellationToken.None);

        Assert.Equal([LoteriaModalityKeys.MegaSena], tracking.ModalitiesCalled);
    }

    [Fact]
    public async Task T3_rotation_mode_advances_scheduler_after_tick()
    {
        var store = new InMemoryLoteriasLoaderSchedulerStore();
        var tracking = new ModalityTrackingApiClient();
        var fn = CreateFunction(tracking, sequentialAllModalities: false, schedulerStore: store);

        await fn.RunAsync(timer: null!, CancellationToken.None);

        Assert.Equal(1, store.CurrentState!.NextModalityIndex);
        Assert.Equal(LoteriaModalityKeys.Lotofacil, store.CurrentState.LastModalityKey);
    }

    [Fact]
    public async Task T4_legacy_mode_executes_all_modalities_in_order()
    {
        var tracking = new ModalityTrackingApiClient();
        var fn = CreateFunction(tracking, sequentialAllModalities: true);

        await fn.RunAsync(timer: null!, CancellationToken.None);

        Assert.Equal(
            new[]
            {
                LoteriaModalityKeys.Lotofacil,
                LoteriaModalityKeys.MegaSena,
                LoteriaModalityKeys.Quina
            },
            tracking.ModalitiesCalled);
    }

    [Fact]
    public async Task T5_rotation_mode_is_default_when_toggle_absent()
    {
        var tracking = new ModalityTrackingApiClient();
        var fn = CreateFunction(tracking, sequentialAllModalities: null);

        await fn.RunAsync(timer: null!, CancellationToken.None);

        Assert.Single(tracking.ModalitiesCalled);
    }

    [Fact]
    public async Task T6_rotation_mode_advances_scheduler_when_use_case_fails()
    {
        var store = new InMemoryLoteriasLoaderSchedulerStore();
        var tracking = new ModalityTrackingApiClient(failModality: LoteriaModalityKeys.Lotofacil);
        var fn = CreateFunction(tracking, sequentialAllModalities: false, schedulerStore: store);

        await fn.RunAsync(timer: null!, CancellationToken.None);

        Assert.Equal(1, store.CurrentState!.NextModalityIndex);
    }

    private static LoteriaLoaderTimerFunction CreateFunction(
        ModalityTrackingApiClient api,
        bool? sequentialAllModalities,
        InMemoryLoteriasLoaderSchedulerStore? schedulerStore = null)
    {
        schedulerStore ??= new InMemoryLoteriasLoaderSchedulerStore();
        var clock = new FakeClock(ContractTestHarness.Utc("2026-04-27T23:30:00Z"));

        var loaderCfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        if (sequentialAllModalities is not null)
        {
            loaderCfg = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["LoteriasLoader:SequentialAllModalities"] = sequentialAllModalities.Value.ToString()
                })
                .Build();
        }

        var validatorCfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Lotodicas:BaseUrl"] = "https://example.invalid",
                ["Lotodicas:Token"] = "test-token",
                ["Storage:ConnectionString"] = "UseDevelopmentStorage=true",
                ["Storage:BlobContainer"] = "loterias",
                ["Storage:LotofacilBlobName"] = "Lotofacil",
                ["Storage:MegasenaBlobName"] = "MegaSena",
                ["Storage:QuinaBlobName"] = "Quina",
                ["Storage:LoteriasStateTable"] = "LoteriasState",
                ["LoteriasLoader:TimerSchedule"] = "0 0 * * * *"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IClock>(clock);
        services.AddSingleton<IDelay>(new FakeDelay(clock));
        services.AddSingleton<IRunContext, AsyncLocalRunContext>();
        services.AddSingleton<ILotteriesApiClient>(api);
        services.AddSingleton<ILoteriasLoaderSchedulerStore>(schedulerStore);
        services.AddSingleton<ILoteriasLoaderScheduler>(sp =>
            new LoteriasModalityRotationScheduler(
                sp.GetRequiredService<ILoteriasLoaderSchedulerStore>(),
                sp.GetRequiredService<IClock>(),
                sp.GetRequiredService<IOptions<LoteriasLoaderOptions>>().Value.ParseModalityOrder()));
        services.AddOptions<LoteriasLoaderOptions>().Bind(loaderCfg.GetSection(LoteriasLoaderOptions.SectionName));

        RegisterUseCase(services, LoteriaModalityKeys.Lotofacil, new LotofacilBlobCatalog());
        RegisterUseCase(services, LoteriaModalityKeys.MegaSena, new MegaSenaBlobCatalog());
        RegisterUseCase(services, LoteriaModalityKeys.Quina, new QuinaBlobCatalog());
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton(new V0EnvironmentValidator(validatorCfg));
        services.AddSingleton<LoteriaLoaderTimerFunction>();

        using var sp = services.BuildServiceProvider(validateScopes: true);
        return sp.GetRequiredService<LoteriaLoaderTimerFunction>();
    }

    private static void RegisterUseCase(
        IServiceCollection services,
        string modalityKey,
        ILoteriaBlobCatalog catalog)
    {
        var seededBlob = SeedBlobDocument(catalog, contestId: 5);

        services.AddKeyedSingleton(modalityKey, (sp, key) =>
        {
            var modality = (string)key!;
            return new LoteriaResultsUpdateUseCase(
                sp.GetRequiredService<ILogger<LoteriaResultsUpdateUseCase>>(),
                sp.GetRequiredService<IRunContext>(),
                sp.GetRequiredService<IClock>(),
                sp.GetRequiredService<IDelay>(),
                sp.GetRequiredService<ILotteriesApiClient>(),
                new InMemoryLoteriaBlobStore(seededBlob),
                new InMemoryLoteriaStateStore(new LoteriaLoaderState(5, "2026-04-01", DateTimeOffset.MinValue, null)),
                catalog,
                modalityKey: modality,
                lotteryApiSegment: modality);
        });
    }

    private static object SeedBlobDocument(ILoteriaBlobCatalog catalog, int contestId)
    {
        var raw = catalog switch
        {
            LotofacilBlobCatalog => MinimalLotofacilContestJson(contestId),
            MegaSenaBlobCatalog => MinimalMegaSenaContestJson(contestId),
            QuinaBlobCatalog => MinimalQuinaContestJson(contestId),
            _ => throw new NotSupportedException($"Catalog type {catalog.GetType().Name} not supported in timer rotation tests.")
        };

        var draw = catalog.ParseContestToDraw(raw);
        var id = catalog.GetContestIdFromDraw(draw);
        return catalog.MergeOrderedDraws(new Dictionary<int, object> { [id] = draw });
    }

    private static string MinimalLotofacilContestJson(int id) =>
        $$"""
        {
          "data": {
            "draw_number": {{id}},
            "draw_date": "2026-04-01",
            "drawing": { "draw": [1,2,3,4,5,6,7,8,9,10,11,12,13,14,15] },
            "prizes": [ { "name": "15 acertos", "winners": 0 } ]
          }
        }
        """;

    private static string MinimalMegaSenaContestJson(int id) =>
        $$"""
        {
          "data": {
            "draw_number": {{id}},
            "draw_date": "2026-04-01",
            "drawing": { "draw": [1,2,3,4,5,6] },
            "prizes": [ { "name": "6 acertos", "winners": 0 } ]
          }
        }
        """;

    private static string MinimalQuinaContestJson(int id) =>
        $$"""
        {
          "data": {
            "draw_number": {{id}},
            "draw_date": "2026-04-01",
            "drawing": { "draw": [1,2,3,4,5] },
            "prizes": [ { "name": "5 acertos", "winners": 0 } ]
          }
        }
        """;

    private sealed class ModalityTrackingApiClient : ILotteriesApiClient
    {
        private readonly string? _failModality;

        public ModalityTrackingApiClient(string? failModality = null) => _failModality = failModality;

        public List<string> ModalitiesCalled { get; } = [];

        public Task<int> GetLatestContestIdAsync(string lotteryApiSegment, CancellationToken ct)
        {
            ModalitiesCalled.Add(lotteryApiSegment);
            if (_failModality is not null && string.Equals(lotteryApiSegment, _failModality, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("simulated API failure");
            }

            return Task.FromResult(5);
        }

        public Task<object> GetContestByIdRawAsync(string lotteryApiSegment, int contestId, CancellationToken ct) =>
            throw new InvalidOperationException("not expected in rotation timer tests");

        public Task<object> GetAllResultsRawAsync(string lotteryApiSegment, CancellationToken ct) =>
            throw new InvalidOperationException("not expected in rotation timer tests");
    }

    private sealed class InMemoryLoteriaBlobStore : ILoteriaBlobStore
    {
        private readonly object _document;

        public InMemoryLoteriaBlobStore(object document) => _document = document;

        public Task<object?> TryReadRawAsync(CancellationToken ct) => Task.FromResult<object?>(_document);

        public Task WriteRawAsync(object document, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class InMemoryLoteriaStateStore : ILoteriaStateStore
    {
        private LoteriaLoaderState _state;

        public InMemoryLoteriaStateStore(LoteriaLoaderState state) => _state = state;

        public Task<object?> TryReadRawAsync(CancellationToken ct) => Task.FromResult<object?>(_state);

        public Task WriteRawAsync(object state, CancellationToken ct)
        {
            _state = (LoteriaLoaderState)state;
            return Task.CompletedTask;
        }
    }
}
