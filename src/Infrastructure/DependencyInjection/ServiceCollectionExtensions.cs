using Lotofacil.Loader.Application;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Lotofacil.Loader.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddLotofacilLoaderV0Infrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<LotodicasOptions>()
            .Bind(configuration.GetSection(LotodicasOptions.SectionName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.BaseUrl), $"{LotodicasOptions.SectionName}__BaseUrl é obrigatório")
            .Validate(o => !string.IsNullOrWhiteSpace(o.Token), $"{LotodicasOptions.SectionName}__Token é obrigatório");

        services.AddOptions<StorageOptions>()
            .Bind(configuration.GetSection(StorageOptions.SectionName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.ConnectionString), $"{StorageOptions.SectionName}__ConnectionString é obrigatório")
            .Validate(o => !string.IsNullOrWhiteSpace(o.BlobContainer), $"{StorageOptions.SectionName}__BlobContainer é obrigatório")
            .Validate(o => !string.IsNullOrWhiteSpace(o.LotofacilBlobName), $"{StorageOptions.SectionName}__LotofacilBlobName é obrigatório")
            .Validate(o => !string.IsNullOrWhiteSpace(o.LotofacilStateTable), $"{StorageOptions.SectionName}__LotofacilStateTable é obrigatório");

        services.AddHttpClient<LotodicasApiClient>((sp, http) =>
        {
            var opt = sp.GetRequiredService<IOptions<LotodicasOptions>>().Value;

            var baseUrl = opt.BaseUrl.Trim();
            if (baseUrl.EndsWith("/", StringComparison.Ordinal))
            {
                baseUrl = baseUrl.TrimEnd('/');
            }

            http.BaseAddress = new Uri(baseUrl, UriKind.Absolute);
            http.DefaultRequestHeaders.Accept.Clear();
            http.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        });

        services.AddSingleton<ILotofacilApiClient>(sp => sp.GetRequiredService<LotodicasApiClient>());
        services.AddSingleton<ILotofacilBlobStore, AzureBlobLotofacilBlobStore>();
        services.AddSingleton<ILotofacilStateStore, AzureTableLotofacilStateStore>();

        return services;
    }
}

