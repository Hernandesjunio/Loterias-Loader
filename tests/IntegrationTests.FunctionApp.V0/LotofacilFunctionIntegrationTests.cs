using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Lotofacil.Loader.Application;
using Lotofacil.Loader.Composition;
using Lotofacil.Loader.FunctionApp;
using Lotofacil.Loader.FunctionApp.Functions;
using Lotofacil.Loader.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IntegrationTests.FunctionApp.V0;

public sealed class LotofacilFunctionIntegrationTests
{
    [SkippableFact]
    public async Task Legacy_sequential_mode_runs_all_modalities_in_one_invocation()
    {
        await RunFullFlowAsync(sequentialAllModalities: true, invocationCount: 1, assertAllModalitiesInSingleRun: true);
    }

    [SkippableFact]
    public async Task Rotation_mode_runs_one_modality_per_invocation_and_advances_scheduler()
    {
        await RunFullFlowAsync(sequentialAllModalities: false, invocationCount: 3, assertAllModalitiesInSingleRun: false);
    }

    private static async Task RunFullFlowAsync(
        bool sequentialAllModalities,
        int invocationCount,
        bool assertAllModalitiesInSingleRun)
    {
        const string token = "test-token";

        var storageConn = Environment.GetEnvironmentVariable("LOT0_AZURITE_CONNECTION_STRING")
                         ?? Environment.GetEnvironmentVariable("AZURITE_CONNECTION_STRING")
                         ?? "UseDevelopmentStorage=true";

        var reachable = await AzuriteProbe.IsStorageReachableAsync(storageConn, CancellationToken.None);
        Skip.IfNot(reachable, "Azurite não está acessível. Inicie o Azurite local e/ou defina AZURITE_CONNECTION_STRING.");

        var containerName = $"loterias-it-{Guid.NewGuid():n}";
        const string lotofacilBlobName = "Lotofacil";
        const string megaBlobName = "MegaSena";
        const string quinaBlobName = "Quina";
        const string tableName = "LoteriasState";

        await using var fake = new LotodicasFakeServer(token)
            .WithAllResponseJson(LoteriaModalityKeys.Lotofacil, AllResultsJsonLotofacil())
            .WithAllResponseJson(LoteriaModalityKeys.MegaSena, AllResultsJsonMegaSena())
            .WithAllResponseJson(LoteriaModalityKeys.Quina, AllResultsJsonQuina());

        await fake.StartAsync(CancellationToken.None);

        var table = new TableClient(storageConn, tableName);
        await table.CreateIfNotExistsAsync();

        await table.UpsertEntityAsync(new TableEntity(LoteriaModalityKeys.Lotofacil, "Loader")
        {
            ["LastLoadedContestId"] = 5,
            ["LastLoadedDrawDate"] = "2026-04-01",
            ["LastUpdatedAtUtc"] = DateTimeOffset.Parse("2026-04-01T00:00:00Z")
        });

        await table.UpsertEntityAsync(new TableEntity(LoteriaModalityKeys.MegaSena, "Loader")
        {
            ["LastLoadedContestId"] = 5,
            ["LastLoadedDrawDate"] = "2026-04-01",
            ["LastUpdatedAtUtc"] = DateTimeOffset.Parse("2026-04-01T00:00:00Z")
        });

        await table.UpsertEntityAsync(new TableEntity(LoteriaModalityKeys.Quina, "Loader")
        {
            ["LastLoadedContestId"] = 5,
            ["LastLoadedDrawDate"] = "2026-04-01",
            ["LastUpdatedAtUtc"] = DateTimeOffset.Parse("2026-04-01T00:00:00Z")
        });

        var blobContainer = new BlobContainerClient(storageConn, containerName);
        await blobContainer.CreateIfNotExistsAsync();

        var lotofacilBlob = blobContainer.GetBlobClient(lotofacilBlobName);
        await lotofacilBlob.UploadAsync(BinaryData.FromString(SeedLotofacilBlobJsonFor5()), overwrite: true);

        var megaBlob = blobContainer.GetBlobClient(megaBlobName);
        await megaBlob.UploadAsync(BinaryData.FromString(SeedMegaSenaBlobJsonFor5()), overwrite: true);

        var quinaBlob = blobContainer.GetBlobClient(quinaBlobName);
        await quinaBlob.UploadAsync(BinaryData.FromString(SeedQuinaBlobJsonFor5()), overwrite: true);

        var infraCfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Lotodicas:BaseUrl"] = fake.BaseUrl.ToString().TrimEnd('/'),
                ["Lotodicas:Token"] = token,
                ["Storage:ConnectionString"] = storageConn,
                ["Storage:BlobContainer"] = containerName,
                ["Storage:LotofacilBlobName"] = lotofacilBlobName,
                ["Storage:MegasenaBlobName"] = megaBlobName,
                ["Storage:QuinaBlobName"] = quinaBlobName,
                ["Storage:LoteriasStateTable"] = tableName,
                ["LoteriasLoader:SequentialAllModalities"] = sequentialAllModalities.ToString()
            })
            .Build();

        var validatorCfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Lotodicas:BaseUrl"] = "https://example.invalid",
                ["Lotodicas:Token"] = token,
                ["Storage:ConnectionString"] = storageConn,
                ["Storage:BlobContainer"] = containerName,
                ["Storage:LotofacilBlobName"] = lotofacilBlobName,
                ["Storage:MegasenaBlobName"] = megaBlobName,
                ["Storage:QuinaBlobName"] = quinaBlobName,
                ["Storage:LoteriasStateTable"] = tableName,
                ["LoteriasLoader__TimerSchedule"] = "0 * * * * *"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLotofacilLoaderV0Core();
        services.AddLotofacilLoaderV0Infrastructure(infraCfg);
        services.AddSingleton(new V0EnvironmentValidator(validatorCfg));
        services.AddSingleton<LoteriaLoaderTimerFunction>();

        await using var sp = services.BuildServiceProvider(validateScopes: true);

        var fn = sp.GetRequiredService<LoteriaLoaderTimerFunction>();
        for (var i = 0; i < invocationCount; i++)
        {
            await fn.RunAsync(timer: null!, ct: CancellationToken.None);
        }

        var calls = fake.Calls;
        if (assertAllModalitiesInSingleRun)
        {
            Assert.Equal(3, calls.Count);
            Assert.Collection(
                calls,
                c =>
                {
                    Assert.Equal("GET", c.Method);
                    Assert.Equal("/api/v2/lotofacil/results/all", c.Path);
                    Assert.Contains("token=", c.QueryString, StringComparison.Ordinal);
                    Assert.Equal(token, c.Token);
                },
                c =>
                {
                    Assert.Equal("GET", c.Method);
                    Assert.Equal("/api/v2/mega_sena/results/all", c.Path);
                    Assert.Equal(token, c.Token);
                },
                c =>
                {
                    Assert.Equal("GET", c.Method);
                    Assert.Equal("/api/v2/quina/results/all", c.Path);
                    Assert.Equal(token, c.Token);
                });
        }
        else
        {
            Assert.Equal(3, calls.Count);
            Assert.Equal("/api/v2/lotofacil/results/all", calls[0].Path);
            Assert.Equal("/api/v2/mega_sena/results/all", calls[1].Path);
            Assert.Equal("/api/v2/quina/results/all", calls[2].Path);

            var scheduler = await table.GetEntityAsync<TableEntity>("_scheduler", "modality_rotation");
            Assert.Equal(0, scheduler.Value.GetInt32("NextModalityIndex"));
            Assert.Equal(LoteriaModalityKeys.Quina, scheduler.Value.GetString("LastModalityKey"));
        }

        var lfJson = (await lotofacilBlob.DownloadContentAsync()).Value.Content.ToString();
        var expectedLf = JsonNode.Parse(ExpectedLotofacilBlobJsonFor1Through7())!;
        Assert.True(JsonNode.DeepEquals(expectedLf, JsonNode.Parse(lfJson)!), lfJson);

        var msJson = (await megaBlob.DownloadContentAsync()).Value.Content.ToString();
        var expectedMs = JsonNode.Parse(ExpectedMegaSenaBlobJsonFor1Through7())!;
        Assert.True(JsonNode.DeepEquals(expectedMs, JsonNode.Parse(msJson)!), msJson);

        var lfState = await table.GetEntityAsync<TableEntity>(LoteriaModalityKeys.Lotofacil, "Loader");
        Assert.Equal(7, lfState.Value.GetInt32("LastLoadedContestId"));
        Assert.Equal("2026-04-27", lfState.Value.GetString("LastLoadedDrawDate"));

        var msState = await table.GetEntityAsync<TableEntity>(LoteriaModalityKeys.MegaSena, "Loader");
        Assert.Equal(7, msState.Value.GetInt32("LastLoadedContestId"));
        Assert.Equal("2026-04-27", msState.Value.GetString("LastLoadedDrawDate"));

        var qnJson = (await quinaBlob.DownloadContentAsync()).Value.Content.ToString();
        var expectedQn = JsonNode.Parse(ExpectedQuinaBlobJsonFor1Through7())!;
        Assert.True(JsonNode.DeepEquals(expectedQn, JsonNode.Parse(qnJson)!), qnJson);

        var qnState = await table.GetEntityAsync<TableEntity>(LoteriaModalityKeys.Quina, "Loader");
        Assert.Equal(7, qnState.Value.GetInt32("LastLoadedContestId"));
        Assert.Equal("2026-04-27", qnState.Value.GetString("LastLoadedDrawDate"));
    }

    private static string AllResultsJsonLotofacil() =>
        JsonSerializer.Serialize(new
        {
            data = Enumerable.Range(1, 7).Select(id => new
            {
                draw_number = id,
                draw_date = id == 7 ? "2026-04-27" : "2026-04-01",
                drawing = new { draw = new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15 } },
                prizes = new[] { new { name = "15 acertos", winners = id == 7 ? 5 : 0 } }
            })
        });

    private static string AllResultsJsonMegaSena() =>
        JsonSerializer.Serialize(new
        {
            data = Enumerable.Range(1, 7).Select(id => new
            {
                draw_number = id,
                draw_date = id == 7 ? "2026-04-27" : "2026-04-01",
                drawing = new { draw = new[] { 1, 2, 3, 4, 5, 6 } },
                prizes = new[] { new { name = "6 acertos", winners = id == 7 ? 2 : 0 } }
            })
        });

    private static string AllResultsJsonQuina() =>
        JsonSerializer.Serialize(new
        {
            data = Enumerable.Range(1, 7).Select(id => new
            {
                draw_number = id,
                draw_date = id == 7 ? "2026-04-27" : "2026-04-01",
                drawing = new { draw = new[] { 1, 2, 3, 4, 5 } },
                prizes = new[] { new { name = "5 acertos", winners = id == 7 ? 3 : 0 } }
            })
        });

    private static string ExpectedLotofacilBlobJsonFor1Through7() =>
        """
        {
          "draws": [
            { "contest_id": 1, "draw_date": "2026-04-01", "numbers": [1,2,3,4,5,6,7,8,9,10,11,12,13,14,15], "winners_15": 0, "has_winner_15": false },
            { "contest_id": 2, "draw_date": "2026-04-01", "numbers": [1,2,3,4,5,6,7,8,9,10,11,12,13,14,15], "winners_15": 0, "has_winner_15": false },
            { "contest_id": 3, "draw_date": "2026-04-01", "numbers": [1,2,3,4,5,6,7,8,9,10,11,12,13,14,15], "winners_15": 0, "has_winner_15": false },
            { "contest_id": 4, "draw_date": "2026-04-01", "numbers": [1,2,3,4,5,6,7,8,9,10,11,12,13,14,15], "winners_15": 0, "has_winner_15": false },
            { "contest_id": 5, "draw_date": "2026-04-01", "numbers": [1,2,3,4,5,6,7,8,9,10,11,12,13,14,15], "winners_15": 0, "has_winner_15": false },
            { "contest_id": 6, "draw_date": "2026-04-01", "numbers": [1,2,3,4,5,6,7,8,9,10,11,12,13,14,15], "winners_15": 0, "has_winner_15": false },
            { "contest_id": 7, "draw_date": "2026-04-27", "numbers": [1,2,3,4,5,6,7,8,9,10,11,12,13,14,15], "winners_15": 5, "has_winner_15": true }
          ]
        }
        """;

    private static string ExpectedMegaSenaBlobJsonFor1Through7() =>
        """
        {
          "draws": [
            { "contest_id": 1, "draw_date": "2026-04-01", "numbers": [1,2,3,4,5,6], "winners_6": 0, "has_winner_6": false },
            { "contest_id": 2, "draw_date": "2026-04-01", "numbers": [1,2,3,4,5,6], "winners_6": 0, "has_winner_6": false },
            { "contest_id": 3, "draw_date": "2026-04-01", "numbers": [1,2,3,4,5,6], "winners_6": 0, "has_winner_6": false },
            { "contest_id": 4, "draw_date": "2026-04-01", "numbers": [1,2,3,4,5,6], "winners_6": 0, "has_winner_6": false },
            { "contest_id": 5, "draw_date": "2026-04-01", "numbers": [1,2,3,4,5,6], "winners_6": 0, "has_winner_6": false },
            { "contest_id": 6, "draw_date": "2026-04-01", "numbers": [1,2,3,4,5,6], "winners_6": 0, "has_winner_6": false },
            { "contest_id": 7, "draw_date": "2026-04-27", "numbers": [1,2,3,4,5,6], "winners_6": 2, "has_winner_6": true }
          ]
        }
        """;

    private static string ExpectedQuinaBlobJsonFor1Through7() =>
        """
        {
          "draws": [
            { "contest_id": 1, "draw_date": "2026-04-01", "numbers": [1,2,3,4,5], "winners_5": 0, "has_winner_5": false },
            { "contest_id": 2, "draw_date": "2026-04-01", "numbers": [1,2,3,4,5], "winners_5": 0, "has_winner_5": false },
            { "contest_id": 3, "draw_date": "2026-04-01", "numbers": [1,2,3,4,5], "winners_5": 0, "has_winner_5": false },
            { "contest_id": 4, "draw_date": "2026-04-01", "numbers": [1,2,3,4,5], "winners_5": 0, "has_winner_5": false },
            { "contest_id": 5, "draw_date": "2026-04-01", "numbers": [1,2,3,4,5], "winners_5": 0, "has_winner_5": false },
            { "contest_id": 6, "draw_date": "2026-04-01", "numbers": [1,2,3,4,5], "winners_5": 0, "has_winner_5": false },
            { "contest_id": 7, "draw_date": "2026-04-27", "numbers": [1,2,3,4,5], "winners_5": 3, "has_winner_5": true }
          ]
        }
        """;

    private static string SeedLotofacilBlobJsonFor5() =>
        """
        {
          "draws": [
            {
              "contest_id": 5,
              "draw_date": "2026-04-01",
              "numbers": [1,2,3,4,5,6,7,8,9,10,11,12,13,14,15],
              "winners_15": 0,
              "has_winner_15": false
            }
          ]
        }
        """;

    private static string SeedMegaSenaBlobJsonFor5() =>
        """
        {
          "draws": [
            {
              "contest_id": 5,
              "draw_date": "2026-04-01",
              "numbers": [1,2,3,4,5,6],
              "winners_6": 0,
              "has_winner_6": false
            }
          ]
        }
        """;

    private static string SeedQuinaBlobJsonFor5() =>
        """
        {
          "draws": [
            {
              "contest_id": 5,
              "draw_date": "2026-04-01",
              "numbers": [1,2,3,4,5],
              "winners_5": 0,
              "has_winner_5": false
            }
          ]
        }
        """;
}
