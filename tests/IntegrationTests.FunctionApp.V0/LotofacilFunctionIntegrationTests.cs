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
    public async Task Full_flow_trigger_runs_lotofacil_and_mega_sena_against_fake_lotodicas_and_persists_blobs_and_table_rows()
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
        const string tableName = "LoteriasState";

        await using var fake = new LotodicasFakeServer(token)
            .WithLatestResponseJson(LoteriaModalityKeys.Lotofacil, LatestJson(latestId: 7))
            .WithLatestResponseJson(LoteriaModalityKeys.MegaSena, LatestJson(latestId: 7))
            .WithContestResponseJson(LoteriaModalityKeys.Lotofacil, 6, ContestJsonLotofacil(id: 6, date: "2026-04-27", winners15: 0))
            .WithContestResponseJson(LoteriaModalityKeys.Lotofacil, 7, ContestJsonLotofacil(id: 7, date: "2026-04-27", winners15: 5))
            .WithContestResponseJson(LoteriaModalityKeys.MegaSena, 6, ContestJsonMegaSena(id: 6, date: "2026-04-27", winners6: 0))
            .WithContestResponseJson(LoteriaModalityKeys.MegaSena, 7, ContestJsonMegaSena(id: 7, date: "2026-04-27", winners6: 2));

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

        var blobContainer = new BlobContainerClient(storageConn, containerName);
        await blobContainer.CreateIfNotExistsAsync();

        var lotofacilBlob = blobContainer.GetBlobClient(lotofacilBlobName);
        await lotofacilBlob.UploadAsync(BinaryData.FromString(SeedLotofacilBlobJsonFor5()), overwrite: true);

        var megaBlob = blobContainer.GetBlobClient(megaBlobName);
        await megaBlob.UploadAsync(BinaryData.FromString(SeedMegaSenaBlobJsonFor5()), overwrite: true);

        var infraCfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Lotodicas:BaseUrl"] = fake.BaseUrl.ToString().TrimEnd('/'),
                ["Lotodicas:Token"] = token,
                ["Storage:ConnectionString"] = storageConn,
                ["Storage:BlobContainer"] = containerName,
                ["Storage:LotofacilBlobName"] = lotofacilBlobName,
                ["Storage:MegasenaBlobName"] = megaBlobName,
                ["Storage:LoteriasStateTable"] = tableName
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
        await fn.RunAsync(timer: null!, ct: CancellationToken.None);

        var calls = fake.Calls;
        Assert.Collection(
            calls,
            c =>
            {
                Assert.Equal("GET", c.Method);
                Assert.Equal("/api/v2/lotofacil/results/last", c.Path);
                Assert.Contains("token=", c.QueryString, StringComparison.Ordinal);
                Assert.Equal(token, c.Token);
            },
            c =>
            {
                Assert.Equal("GET", c.Method);
                Assert.Equal("/api/v2/lotofacil/results/6", c.Path);
                Assert.Equal(token, c.Token);
            },
            c =>
            {
                Assert.Equal("GET", c.Method);
                Assert.Equal("/api/v2/lotofacil/results/7", c.Path);
                Assert.Equal(token, c.Token);
            },
            c =>
            {
                Assert.Equal("GET", c.Method);
                Assert.Equal("/api/v2/mega_sena/results/last", c.Path);
                Assert.Equal(token, c.Token);
            },
            c =>
            {
                Assert.Equal("GET", c.Method);
                Assert.Equal("/api/v2/mega_sena/results/6", c.Path);
                Assert.Equal(token, c.Token);
            },
            c =>
            {
                Assert.Equal("GET", c.Method);
                Assert.Equal("/api/v2/mega_sena/results/7", c.Path);
                Assert.Equal(token, c.Token);
            }
        );

        var lfJson = (await lotofacilBlob.DownloadContentAsync()).Value.Content.ToString();
        var expectedLf = JsonNode.Parse(ExpectedLotofacilBlobJsonFor6And7())!;
        Assert.True(JsonNode.DeepEquals(expectedLf, JsonNode.Parse(lfJson)!), lfJson);

        var msJson = (await megaBlob.DownloadContentAsync()).Value.Content.ToString();
        var expectedMs = JsonNode.Parse(ExpectedMegaSenaBlobJsonFor6And7())!;
        Assert.True(JsonNode.DeepEquals(expectedMs, JsonNode.Parse(msJson)!), msJson);

        var lfState = await table.GetEntityAsync<TableEntity>(LoteriaModalityKeys.Lotofacil, "Loader");
        Assert.Equal(7, lfState.Value.GetInt32("LastLoadedContestId"));
        Assert.Equal("2026-04-27", lfState.Value.GetString("LastLoadedDrawDate"));

        var msState = await table.GetEntityAsync<TableEntity>(LoteriaModalityKeys.MegaSena, "Loader");
        Assert.Equal(7, msState.Value.GetInt32("LastLoadedContestId"));
        Assert.Equal("2026-04-27", msState.Value.GetString("LastLoadedDrawDate"));
    }

    private static string LatestJson(int latestId) =>
        JsonSerializer.Serialize(new { data = new { draw_number = latestId } });

    private static string ContestJsonLotofacil(int id, string date, int winners15)
    {
        var obj = new
        {
            data = new
            {
                draw_number = id,
                draw_date = date,
                drawing = new { draw = new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15 } },
                prizes = new[] { new { name = "15 acertos", winners = winners15 } }
            }
        };
        return JsonSerializer.Serialize(obj);
    }

    private static string ContestJsonMegaSena(int id, string date, int winners6)
    {
        var obj = new
        {
            data = new
            {
                draw_number = id,
                draw_date = date,
                drawing = new { draw = new[] { 1, 2, 3, 4, 5, 6 } },
                prizes = new[] { new { name = "6 acertos", winners = winners6 } }
            }
        };
        return JsonSerializer.Serialize(obj);
    }

    private static string ExpectedLotofacilBlobJsonFor6And7() =>
        """
        {
          "draws": [
            {
              "contest_id": 6,
              "draw_date": "2026-04-27",
              "numbers": [1,2,3,4,5,6,7,8,9,10,11,12,13,14,15],
              "winners_15": 0,
              "has_winner_15": false
            },
            {
              "contest_id": 7,
              "draw_date": "2026-04-27",
              "numbers": [1,2,3,4,5,6,7,8,9,10,11,12,13,14,15],
              "winners_15": 5,
              "has_winner_15": true
            }
          ]
        }
        """;

    private static string ExpectedMegaSenaBlobJsonFor6And7() =>
        """
        {
          "draws": [
            {
              "contest_id": 6,
              "draw_date": "2026-04-27",
              "numbers": [1,2,3,4,5,6],
              "winners_6": 0,
              "has_winner_6": false
            },
            {
              "contest_id": 7,
              "draw_date": "2026-04-27",
              "numbers": [1,2,3,4,5,6],
              "winners_6": 2,
              "has_winner_6": true
            }
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
}
