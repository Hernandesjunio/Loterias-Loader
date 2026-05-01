using System.Text.Json;
using Lotofacil.Loader.Domain;

namespace Lotofacil.Loader.Application;

public sealed class LotofacilBlobCatalog : ILoteriaBlobCatalog
{
    public object EmptyDocument() =>
        new LotofacilBlobDocument(Array.Empty<LotofacilBlobDraw>());

    public object ParseDocument(object raw) => raw switch
    {
        LotofacilBlobDocument doc => doc,
        string s => JsonSerializer.Deserialize<LotofacilBlobDocument>(s, JsonOptions()) ??
                    new LotofacilBlobDocument(Array.Empty<LotofacilBlobDraw>()),
        JsonDocument jd => jd.RootElement.Deserialize<LotofacilBlobDocument>(JsonOptions()) ??
                           new LotofacilBlobDocument(Array.Empty<LotofacilBlobDraw>()),
        JsonElement je => je.Deserialize<LotofacilBlobDocument>(JsonOptions()) ??
                          new LotofacilBlobDocument(Array.Empty<LotofacilBlobDraw>()),
        _ => JsonSerializer.Deserialize<LotofacilBlobDocument>(
                  JsonSerializer.Serialize(raw, JsonOptions()),
                  JsonOptions()) ??
              new LotofacilBlobDocument(Array.Empty<LotofacilBlobDraw>())
    };

    public object ParseContestToDraw(object rawContest)
    {
        JsonElement root = ToRootElement(rawContest);
        var data = root.GetProperty("data");

        var contestId = data.GetProperty("draw_number").GetInt32();
        var drawDate = data.GetProperty("draw_date").GetString() ?? throw new InvalidOperationException("draw_date null");

        var numbersArr = data.GetProperty("drawing").GetProperty("draw");
        var numbers = numbersArr.EnumerateArray().Select(x => x.GetInt32()).ToArray();

        var winners15 = 0;
        foreach (var prize in data.GetProperty("prizes").EnumerateArray())
        {
            var name = prize.GetProperty("name").GetString();
            if (string.Equals(name, "15 acertos", StringComparison.Ordinal))
            {
                winners15 = prize.GetProperty("winners").GetInt32();
                break;
            }
        }

        return new LotofacilBlobDraw(
            ContestId: contestId,
            DrawDate: drawDate,
            Numbers: numbers,
            Winners15: winners15,
            HasWinner15: winners15 > 0
        );
    }

    public int GetContestIdFromDraw(object draw) =>
        draw is LotofacilBlobDraw d ? d.ContestId : throw new InvalidOperationException($"Expected {nameof(LotofacilBlobDraw)}.");

    public string? GetDrawDateFromDraw(object draw) =>
        draw is LotofacilBlobDraw d ? d.DrawDate : throw new InvalidOperationException($"Expected {nameof(LotofacilBlobDraw)}.");

    public object MergeOrderedDraws(IReadOnlyDictionary<int, object> drawsByContestId)
    {
        var list = drawsByContestId.Values.Cast<LotofacilBlobDraw>().OrderBy(x => x.ContestId).ToArray();
        return new LotofacilBlobDocument(list);
    }

    private static JsonElement ToRootElement(object rawContest)
    {
        if (rawContest is JsonDocument jd)
        {
            return jd.RootElement.Clone();
        }

        if (rawContest is JsonElement je)
        {
            return je.Clone();
        }

        if (rawContest is string s)
        {
            using var doc = JsonDocument.Parse(s);
            return doc.RootElement.Clone();
        }

        var json = JsonSerializer.Serialize(rawContest, JsonOptions());
        using var d = JsonDocument.Parse(json);
        return d.RootElement.Clone();
    }

    private static JsonSerializerOptions JsonOptions() => new()
    {
        PropertyNameCaseInsensitive = true
    };
}
