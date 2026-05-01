using Lotofacil.Loader.Application;
using Lotofacil.Loader.Composition;
using Microsoft.Extensions.DependencyInjection;

namespace Lotofacil.Loader.V0.Contract;

public static class EntryPoint
{
    public static IServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddLotofacilLoaderV0Core();
        return services.BuildServiceProvider(validateScopes: true);
    }

    public static Task RunAsync(
        ILotteriesApiClient api,
        ILoteriaBlobStore blob,
        ILoteriaStateStore state,
        CancellationToken ct)
    {
        var services = new ServiceCollection();
        services.AddLotofacilLoaderV0Core();
        services.AddSingleton(api);
        services.AddSingleton(blob);
        services.AddSingleton(state);
        services.AddSingleton<LotofacilBlobCatalog>();
        services.AddSingleton(sp => new LoteriaResultsUpdateUseCase(
            sp.GetRequiredService<IClock>(),
            sp.GetRequiredService<IDelay>(),
            sp.GetRequiredService<ILotteriesApiClient>(),
            sp.GetRequiredService<ILoteriaBlobStore>(),
            sp.GetRequiredService<ILoteriaStateStore>(),
            sp.GetRequiredService<LotofacilBlobCatalog>(),
            modalityKey: LoteriaModalityKeys.Lotofacil,
            lotteryApiSegment: LoteriaModalityKeys.Lotofacil));

        using var sp = services.BuildServiceProvider(validateScopes: true);
        var uc = sp.GetRequiredService<LoteriaResultsUpdateUseCase>();
        return RunAsyncInternal(uc, ct);
    }

    public static Task RunAsync(
        ILotteriesApiClient api,
        ILoteriaBlobStore blob,
        ILoteriaStateStore state,
        IClock clock,
        IDelay delay,
        CancellationToken ct)
    {
        var services = new ServiceCollection();
        services.AddLotofacilLoaderV0Core();
        services.AddSingleton(api);
        services.AddSingleton(blob);
        services.AddSingleton(state);
        services.AddSingleton(clock);
        services.AddSingleton(delay);
        services.AddSingleton<LotofacilBlobCatalog>();
        services.AddSingleton(sp => new LoteriaResultsUpdateUseCase(
            sp.GetRequiredService<IClock>(),
            sp.GetRequiredService<IDelay>(),
            sp.GetRequiredService<ILotteriesApiClient>(),
            sp.GetRequiredService<ILoteriaBlobStore>(),
            sp.GetRequiredService<ILoteriaStateStore>(),
            sp.GetRequiredService<LotofacilBlobCatalog>(),
            modalityKey: LoteriaModalityKeys.Lotofacil,
            lotteryApiSegment: LoteriaModalityKeys.Lotofacil));

        using var sp = services.BuildServiceProvider(validateScopes: true);
        var uc = sp.GetRequiredService<LoteriaResultsUpdateUseCase>();
        return RunAsyncInternal(uc, ct);
    }

    private static async Task RunAsyncInternal(LoteriaResultsUpdateUseCase uc, CancellationToken ct)
    {
        _ = await uc.ExecuteAsync(ct);
    }
}
