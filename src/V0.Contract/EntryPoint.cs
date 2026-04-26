using Lotofacil.Loader.Application;
using Lotofacil.Loader.Composition;
using Microsoft.Extensions.DependencyInjection;

namespace Lotofacil.Loader.V0.Contract;

public static class EntryPoint
{
    /// <summary>
    /// Ponto de acoplamento mínimo para testes de contrato (sem Azure).
    /// A implementação completa da V0 será materializada por fatias.
    /// </summary>
    public static IServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddLotofacilLoaderV0Core();

        // Sem adaptadores reais ainda: as portas precisam ser fornecidas por tests/fakes
        // ou pela infra quando existir. Aqui intencionalmente não registramos nada.
        return services.BuildServiceProvider(validateScopes: true);
    }

    public static Task RunAsync(
        ILotofacilApiClient api,
        ILotofacilBlobStore blob,
        ILotofacilStateStore state,
        CancellationToken ct)
    {
        var services = new ServiceCollection();
        services.AddLotofacilLoaderV0Core();
        services.AddSingleton(api);
        services.AddSingleton(blob);
        services.AddSingleton(state);

        using var sp = services.BuildServiceProvider(validateScopes: true);
        var uc = sp.GetRequiredService<UpdateLotofacilResultsUseCase>();
        return RunAsyncInternal(uc, ct);
    }

    public static Task RunAsync(
        ILotofacilApiClient api,
        ILotofacilBlobStore blob,
        ILotofacilStateStore state,
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

        using var sp = services.BuildServiceProvider(validateScopes: true);
        var uc = sp.GetRequiredService<UpdateLotofacilResultsUseCase>();
        return RunAsyncInternal(uc, ct);
    }

    private static async Task RunAsyncInternal(UpdateLotofacilResultsUseCase uc, CancellationToken ct)
    {
        _ = await uc.ExecuteAsync(ct);
    }
}

