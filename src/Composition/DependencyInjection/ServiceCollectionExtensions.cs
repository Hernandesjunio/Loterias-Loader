using Lotofacil.Loader.Application;
using Microsoft.Extensions.DependencyInjection;

namespace Lotofacil.Loader.Composition;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddLotofacilLoaderV0Core(this IServiceCollection services)
    {
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<UpdateLotofacilResultsUseCase>();
        return services;
    }

    private sealed class SystemClock : IClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    }
}

