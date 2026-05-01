using System.Text.Json;
using Lotofacil.Loader.Domain;

namespace Lotofacil.Loader.Application;

public sealed class MegaSenaBlobCatalog : ILoteriaBlobCatalog
{
    public object EmptyDocument() =>
        new MegaSenaBlobDocument(Array.Empty<MegaSenaBlobDraw>());

    public object ParseDocument(object raw) => raw switch
    {
        MegaSenaBlobDocument doc => doc,
        string s => JsonSerializer.Deserialize<MegaSenaBlobDocument>(s, JsonOptions()) ??
                    new MegaSenaBlobDocument(Array.Empty<MegaSenaBlobDraw>()),
        JsonDocument jd => jd.RootElement.Deserialize<MegaSenaBlobDocument>(JsonOptions()) ??
                           new MegaSenaBlobDocument(Array.Empty<MegaSenaBlobDraw>()),
        JsonElement je => je.Deserialize<MegaSenaBlobDocument>(JsonOptions()) ??
                          new MegaSenaBlobDocument(Array.Empty<MegaSenaBlobDraw>()),
        _ => JsonSerializer.Deserialize<MegaSenaBlobDocument>(
                  JsonSerializer.Serialize(raw, JsonOptions()),
                  JsonOptions()) ??
              new MegaSenaBlobDocument(Array.Empty<MegaSenaBlobDraw>())
    };

    public object ParseContestToDraw(object rawContest)
    {
        JsonElement root = ToRootElement(rawContest);
        var data = root.GetProperty("data");

        var contestId = data.GetProperty("draw_number").GetInt32();
        var drawDate = data.GetProperty("draw_date").GetString() ?? throw new InvalidOperationException("draw_date null");

        var numbersArr = data.GetProperty("drawing").GetProperty("draw");
        var numbers = numbersArr.EnumerateArray().Select(x => x.GetInt32()).ToArray();

        var winners6 = 0;
        foreach (var prize in data.GetProperty("prizes").EnumerateArray())
        {
            var name = prize.GetProperty("name").GetString();
            if (name is not null && name.Contains("6 acertos", StringComparison.OrdinalIgnoreCase))
            {
                winners6 = prize.GetProperty("winners").GetInt32();
                break;
            }
        }

        return new MegaSenaBlobDraw(
            ContestId: contestId,
            DrawDate: drawDate,
            Numbers: numbers,
            Winners6: winners6,
            HasWinner6: winners6 > 0
        );
    }

    public int GetContestIdFromDraw(object draw) =>
        draw is MegaSenaBlobDraw d ? d.ContestId : throw new InvalidOperationException($"Expected {nameof(MegaSenaBlobDraw)}.");

    public string? GetDrawDateFromDraw(object draw) =>
        draw is MegaSenaBlobDraw d ? d.DrawDate : throw new InvalidOperationException($"Expected {nameof(MegaSenaBlobDraw)}.");

    public object MergeOrderedDraws(IReadOnlyDictionary<int, object> drawsByContestId)
    {
        var list = drawsByContestId.Values.Cast<MegaSenaBlobDraw>().OrderBy(x => x.ContestId).ToArray();
        return new MegaSenaBlobDocument(list);
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
