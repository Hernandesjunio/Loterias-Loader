using System.Diagnostics;
using Lotofacil.Loader.Application;
using Lotofacil.Loader.Domain;
using Lotofacil.Loader.V0.Contract;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace ContractTests.V0;

public sealed class ObservabilityTests
{
    [Fact]
    public async Task Tracing_creates_root_activity_with_minimum_tags_and_events_and_debug_logs()
    {
        var clock = new FakeClock(Utc("2026-04-26T23:00:00Z")); // Domingo (early-exit not business day)
        var delay = new FakeDelay(clock);
        var api = new FakeApi(latestId: 10);
        var seq = new EventSequencer();
        var blob = new InMemoryBlobStore(seq);
        var state = new InMemoryStateStore(seq, existing: new LoteriaLoaderState(5, null, clock.UtcNow, null));

        var started = new List<Activity>();
        var stopped = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == LotofacilLoaderActivitySource.Name,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = a => started.Add(a),
            ActivityStopped = a => stopped.Add(a)
        };
        ActivitySource.AddActivityListener(listener);

        var loggerProvider = new InMemoryLoggerProvider(minLevel: LogLevel.Debug);
        using var loggerFactory = LoggerFactory.Create(b => b.AddProvider(loggerProvider).SetMinimumLevel(LogLevel.Debug));

        var services = new ServiceCollection();
        services.AddSingleton<IClock>(clock);
        services.AddSingleton<IDelay>(delay);
        services.AddSingleton(api);
        services.AddSingleton<ILotteriesApiClient>(sp => sp.GetRequiredService<FakeApi>());
        services.AddSingleton<ILoteriaBlobStore>(blob);
        services.AddSingleton<ILoteriaStateStore>(state);
        services.AddSingleton<ILoteriaBlobCatalog, LotofacilBlobCatalog>();
        services.AddSingleton<IRunContext, AsyncLocalRunContext>();
        services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
        services.AddSingleton<ILoggerFactory>(loggerFactory);
        services.AddSingleton(sp => new LoteriaResultsUpdateUseCase(
            sp.GetRequiredService<ILogger<LoteriaResultsUpdateUseCase>>(),
            sp.GetRequiredService<IRunContext>(),
            sp.GetRequiredService<IClock>(),
            sp.GetRequiredService<IDelay>(),
            sp.GetRequiredService<ILotteriesApiClient>(),
            sp.GetRequiredService<ILoteriaBlobStore>(),
            sp.GetRequiredService<ILoteriaStateStore>(),
            sp.GetRequiredService<ILoteriaBlobCatalog>(),
            disableBusinessDayGuard: false,
            disable20hGuard: false,
            modalityKey: LoteriaModalityKeys.Lotofacil,
            lotteryApiSegment: LoteriaModalityKeys.Lotofacil));

        await using var sp = services.BuildServiceProvider(validateScopes: true);

        var runId = "run-test-001";
        using (sp.GetRequiredService<IRunContext>().BeginRun(runId, LoteriaModalityKeys.Lotofacil))
        {
            _ = await sp.GetRequiredService<LoteriaResultsUpdateUseCase>().ExecuteAsync(CancellationToken.None);
        }

        var root = Assert.Single(stopped, a => a.OperationName == "LotofacilLoader.UpdateResults");
        Assert.Equal(runId, root.GetTagItem("run_id") as string);
        Assert.Equal(LoteriaModalityKeys.Lotofacil, root.GetTagItem("modality") as string);

        Assert.Equal("America/Sao_Paulo", root.GetTagItem("timezone") as string);
        Assert.Equal(180, root.GetTagItem("deadline_seconds"));
        Assert.Equal(false, root.GetTagItem("disable_business_day_guard"));
        Assert.Equal(false, root.GetTagItem("disable_20h_guard"));

        Assert.Equal(ReasonStop.EARLY_EXIT_NOT_BUSINESS_DAY.ToString(), root.GetTagItem("reason_stop") as string);
        Assert.NotNull(root.GetTagItem("retries_count"));
        Assert.NotNull(root.GetTagItem("rate_limit_wait_seconds_total"));
        Assert.NotNull(root.GetTagItem("elapsed_seconds"));

        Assert.Contains(root.Events, e => e.Name == "guards.evaluate");
        Assert.Contains(root.Events, e => e.Name == "stop");

        Assert.Contains(loggerProvider.Entries, e => e.Level == LogLevel.Debug && e.Message.Contains("guards.evaluate", StringComparison.OrdinalIgnoreCase) == false);
        Assert.Contains(loggerProvider.Entries, e => e.Level == LogLevel.Debug && e.Message.Contains("guards.early_exit", StringComparison.Ordinal));
    }

    private static DateTimeOffset Utc(string iso8601Utc) =>
        DateTimeOffset.Parse(iso8601Utc, null, System.Globalization.DateTimeStyles.AssumeUniversal);

    private sealed class FakeClock : IClock
    {
        public FakeClock(DateTimeOffset utcNow) => UtcNow = utcNow;
        public DateTimeOffset UtcNow { get; private set; }
    }

    private sealed class FakeDelay : IDelay
    {
        public FakeDelay(FakeClock clock) { }
        public Task DelayAsync(TimeSpan delay, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class FakeApi : ILotteriesApiClient
    {
        private readonly int _latestId;
        public FakeApi(int latestId) => _latestId = latestId;
        public Task<int> GetLatestContestIdAsync(string lotteryApiSegment, CancellationToken ct) => Task.FromResult(_latestId);
        public Task<object> GetContestByIdRawAsync(string lotteryApiSegment, int contestId, CancellationToken ct) => throw new NotSupportedException();
        public Task<object> GetAllResultsRawAsync(string lotteryApiSegment, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class InMemoryBlobStore : ILoteriaBlobStore
    {
        public InMemoryBlobStore(EventSequencer seq) { }
        public Task<object?> TryReadRawAsync(CancellationToken ct) => Task.FromResult<object?>(new LotofacilBlobDocument(Array.Empty<LotofacilBlobDraw>()));
        public Task WriteRawAsync(object document, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class InMemoryStateStore : ILoteriaStateStore
    {
        private readonly LoteriaLoaderState? _existing;
        public InMemoryStateStore(EventSequencer seq, LoteriaLoaderState? existing) => _existing = existing;
        public Task<object?> TryReadRawAsync(CancellationToken ct) => Task.FromResult<object?>(_existing);
        public Task WriteRawAsync(object state, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class EventSequencer { }

    private sealed class InMemoryLoggerProvider : ILoggerProvider
    {
        private readonly LogLevel _minLevel;
        public InMemoryLoggerProvider(LogLevel minLevel) => _minLevel = minLevel;
        public List<Entry> Entries { get; } = new();
        public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName, _minLevel);
        public void Dispose() { }

        public sealed record Entry(LogLevel Level, string Category, string Message);

        private sealed class Logger : ILogger
        {
            private readonly InMemoryLoggerProvider _provider;
            private readonly string _category;
            private readonly LogLevel _minLevel;

            public Logger(InMemoryLoggerProvider provider, string category, LogLevel minLevel)
            {
                _provider = provider;
                _category = category;
                _minLevel = minLevel;
            }

            public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
            public bool IsEnabled(LogLevel logLevel) => logLevel >= _minLevel;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (!IsEnabled(logLevel))
                {
                    return;
                }

                _provider.Entries.Add(new Entry(logLevel, _category, formatter(state, exception)));
            }
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}

