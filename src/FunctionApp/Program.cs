using Lotofacil.Loader.Composition;
using Lotofacil.Loader.FunctionApp;
using Lotofacil.Loader.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var host = new HostBuilder()
    .ConfigureAppConfiguration(c =>
    {
        c.AddEnvironmentVariables();
    })
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices((ctx, services) =>
    {
        services.AddLotofacilLoaderV0Core();
        services.AddLotofacilLoaderV0Infrastructure(ctx.Configuration);
        services.AddSingleton<V0EnvironmentValidator>();
    })
    .Build();

host.Run();
