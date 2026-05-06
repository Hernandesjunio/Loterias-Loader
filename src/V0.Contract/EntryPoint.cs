using Lotofacil.Loader.Application;
using Lotofacil.Loader.Composition;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Lotofacil.Loader.V0.Contract;

public static class EntryPoint
{
    public static IServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLotofacilLoaderV0Core();
        return services.BuildServiceProvider(validateScopes: true);
    }

    public static Task RunAsync(
        ILotteriesApiClient api,
        ILoteriaBlobStore blob,
        ILoteriaStateStore state,
        CancellationToken ct)
        => RunAsync(api, blob, state, disableBusinessDayGuard: false, disable20hGuard: false, ct);

    public static Task RunAsync(
        ILotteriesApiClient api,
        ILoteriaBlobStore blob,
        ILoteriaStateStore state,
        bool disableBusinessDayGuard,
        bool disable20hGuard,
        CancellationToken ct)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLotofacilLoaderV0Core();
        services.AddSingleton(api);
        services.AddSingleton(blob);
        services.AddSingleton(state);
        services.AddSingleton<LotofacilBlobCatalog>();
        services.AddSingleton(sp => new LoteriaResultsUpdateUseCase(
            sp.GetRequiredService<ILogger<LoteriaResultsUpdateUseCase>>(),
            sp.GetRequiredService<IRunContext>(),
            sp.GetRequiredService<IClock>(),
            sp.GetRequiredService<IDelay>(),
            sp.GetRequiredService<ILotteriesApiClient>(),
            sp.GetRequiredService<ILoteriaBlobStore>(),
            sp.GetRequiredService<ILoteriaStateStore>(),
            sp.GetRequiredService<LotofacilBlobCatalog>(),
            disableBusinessDayGuard: disableBusinessDayGuard,
            disable20hGuard: disable20hGuard,
            modalityKey: LoteriaModalityKeys.Lotofacil,
            lotteryApiSegment: LoteriaModalityKeys.Lotofacil));

        using var sp = services.BuildServiceProvider(validateScopes: true);
        var uc = sp.GetRequiredService<LoteriaResultsUpdateUseCase>();
        return RunAsyncInternal(sp, uc, ct);
    }

    public static Task RunAsync(
        ILotteriesApiClient api,
        ILoteriaBlobStore blob,
        ILoteriaStateStore state,
        IClock clock,
        IDelay delay,
        CancellationToken ct)
        => RunAsync(api, blob, state, clock, delay, disableBusinessDayGuard: false, disable20hGuard: false, ct);

    public static Task RunAsync(
        ILotteriesApiClient api,
        ILoteriaBlobStore blob,
        ILoteriaStateStore state,
        IClock clock,
        IDelay delay,
        bool disableBusinessDayGuard,
        bool disable20hGuard,
        CancellationToken ct)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLotofacilLoaderV0Core();
        services.AddSingleton(api);
        services.AddSingleton(blob);
        services.AddSingleton(state);
        services.AddSingleton(clock);
        services.AddSingleton(delay);
        services.AddSingleton<LotofacilBlobCatalog>();
        services.AddSingleton(sp => new LoteriaResultsUpdateUseCase(
            sp.GetRequiredService<ILogger<LoteriaResultsUpdateUseCase>>(),
            sp.GetRequiredService<IRunContext>(),
            sp.GetRequiredService<IClock>(),
            sp.GetRequiredService<IDelay>(),
            sp.GetRequiredService<ILotteriesApiClient>(),
            sp.GetRequiredService<ILoteriaBlobStore>(),
            sp.GetRequiredService<ILoteriaStateStore>(),
            sp.GetRequiredService<LotofacilBlobCatalog>(),
            disableBusinessDayGuard: disableBusinessDayGuard,
            disable20hGuard: disable20hGuard,
            modalityKey: LoteriaModalityKeys.Lotofacil,
            lotteryApiSegment: LoteriaModalityKeys.Lotofacil));

        using var sp = services.BuildServiceProvider(validateScopes: true);
        var uc = sp.GetRequiredService<LoteriaResultsUpdateUseCase>();
        return RunAsyncInternal(sp, uc, ct);
    }

    private static async Task RunAsyncInternal(IServiceProvider sp, LoteriaResultsUpdateUseCase uc, CancellationToken ct)
    {
        var runId = Guid.NewGuid().ToString("n");
        using var runScope = sp.GetRequiredService<IRunContext>().BeginRun(runId, uc.ModalityKey);
        _ = await uc.ExecuteAsync(ct);
    }
}
