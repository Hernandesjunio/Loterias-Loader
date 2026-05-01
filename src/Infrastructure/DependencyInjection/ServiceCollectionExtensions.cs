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
            .Validate(o => !string.IsNullOrWhiteSpace(o.MegasenaBlobName), $"{StorageOptions.SectionName}__MegasenaBlobName é obrigatório")
            .Validate(o => !string.IsNullOrWhiteSpace(o.LoteriasStateTable), $"{StorageOptions.SectionName}__LoteriasStateTable é obrigatório");

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

        services.AddSingleton<ILotteriesApiClient>(sp => sp.GetRequiredService<LotodicasApiClient>());

        services.AddSingleton<LotofacilBlobCatalog>();
        services.AddSingleton<MegaSenaBlobCatalog>();

        services.AddKeyedSingleton<ILoteriaBlobStore>(LoteriaModalityKeys.Lotofacil, static (sp, _) =>
        {
            var o = sp.GetRequiredService<IOptions<StorageOptions>>().Value;
            return new AzureBlobLoteriaBlobStore(o.ConnectionString, o.BlobContainer, o.LotofacilBlobName);
        });

        services.AddKeyedSingleton<ILoteriaBlobStore>(LoteriaModalityKeys.MegaSena, static (sp, _) =>
        {
            var o = sp.GetRequiredService<IOptions<StorageOptions>>().Value;
            return new AzureBlobLoteriaBlobStore(o.ConnectionString, o.BlobContainer, o.MegasenaBlobName);
        });

        services.AddKeyedSingleton<ILoteriaStateStore>(LoteriaModalityKeys.Lotofacil, static (sp, _) =>
        {
            var o = sp.GetRequiredService<IOptions<StorageOptions>>().Value;
            return new AzureTableLoteriaStateStore(o.ConnectionString, o.LoteriasStateTable, LoteriaModalityKeys.Lotofacil);
        });

        services.AddKeyedSingleton<ILoteriaStateStore>(LoteriaModalityKeys.MegaSena, static (sp, _) =>
        {
            var o = sp.GetRequiredService<IOptions<StorageOptions>>().Value;
            return new AzureTableLoteriaStateStore(o.ConnectionString, o.LoteriasStateTable, LoteriaModalityKeys.MegaSena);
        });

        services.AddKeyedSingleton<LoteriaResultsUpdateUseCase>(LoteriaModalityKeys.Lotofacil, static (sp, _) =>
            CreateUseCase(
                sp,
                modalityKey: LoteriaModalityKeys.Lotofacil,
                lotteryApiSegment: LoteriaModalityKeys.Lotofacil,
                catalog: sp.GetRequiredService<LotofacilBlobCatalog>()));

        services.AddKeyedSingleton<LoteriaResultsUpdateUseCase>(LoteriaModalityKeys.MegaSena, static (sp, _) =>
            CreateUseCase(
                sp,
                modalityKey: LoteriaModalityKeys.MegaSena,
                lotteryApiSegment: LoteriaModalityKeys.MegaSena,
                catalog: sp.GetRequiredService<MegaSenaBlobCatalog>()));

        return services;
    }

    private static LoteriaResultsUpdateUseCase CreateUseCase(
        IServiceProvider sp,
        string modalityKey,
        string lotteryApiSegment,
        ILoteriaBlobCatalog catalog) =>
        new(
            sp.GetRequiredService<IClock>(),
            sp.GetRequiredService<IDelay>(),
            sp.GetRequiredService<ILotteriesApiClient>(),
            sp.GetRequiredKeyedService<ILoteriaBlobStore>(modalityKey),
            sp.GetRequiredKeyedService<ILoteriaStateStore>(modalityKey),
            catalog,
            modalityKey: modalityKey,
            lotteryApiSegment: lotteryApiSegment);
}
