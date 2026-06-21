using System.Text.Json;
using Lotofacil.Loader.Domain;

namespace Lotofacil.Loader.Application;

public sealed class QuinaBlobCatalog : ILoteriaBlobCatalog
{
    public object EmptyDocument() =>
        new QuinaBlobDocument(Array.Empty<QuinaBlobDraw>());

    public object ParseDocument(object raw) => raw switch
    {
        QuinaBlobDocument doc => doc,
        string s => JsonSerializer.Deserialize<QuinaBlobDocument>(s, JsonOptions()) ??
                    new QuinaBlobDocument(Array.Empty<QuinaBlobDraw>()),
        JsonDocument jd => jd.RootElement.Deserialize<QuinaBlobDocument>(JsonOptions()) ??
                           new QuinaBlobDocument(Array.Empty<QuinaBlobDraw>()),
        JsonElement je => je.Deserialize<QuinaBlobDocument>(JsonOptions()) ??
                          new QuinaBlobDocument(Array.Empty<QuinaBlobDraw>()),
        _ => JsonSerializer.Deserialize<QuinaBlobDocument>(
                  JsonSerializer.Serialize(raw, JsonOptions()),
                  JsonOptions()) ??
              new QuinaBlobDocument(Array.Empty<QuinaBlobDraw>())
    };

    public object ParseContestToDraw(object rawContest)
    {
        JsonElement root = ToRootElement(rawContest);
        var data = root.GetProperty("data");

        var contestId = data.GetProperty("draw_number").GetInt32();
        var drawDate = data.GetProperty("draw_date").GetString() ?? throw new InvalidOperationException("draw_date null");

        var numbersArr = data.GetProperty("drawing").GetProperty("draw");
        var numbers = numbersArr.EnumerateArray().Select(x => x.GetInt32()).ToArray();

        var winners5 = 0;
        foreach (var prize in data.GetProperty("prizes").EnumerateArray())
        {
            var name = prize.GetProperty("name").GetString();
            if (name is not null && name.Contains("5 acertos", StringComparison.OrdinalIgnoreCase))
            {
                winners5 = prize.GetProperty("winners").GetInt32();
                break;
            }
        }

        return new QuinaBlobDraw(
            ContestId: contestId,
            DrawDate: drawDate,
            Numbers: numbers,
            Winners5: winners5,
            HasWinner5: winners5 > 0
        );
    }

    public int GetContestIdFromDraw(object draw) =>
        draw is QuinaBlobDraw d ? d.ContestId : throw new InvalidOperationException($"Expected {nameof(QuinaBlobDraw)}.");

    public string? GetDrawDateFromDraw(object draw) =>
        draw is QuinaBlobDraw d ? d.DrawDate : throw new InvalidOperationException($"Expected {nameof(QuinaBlobDraw)}.");

    public object MergeOrderedDraws(IReadOnlyDictionary<int, object> drawsByContestId)
    {
        var list = drawsByContestId.Values.Cast<QuinaBlobDraw>().OrderBy(x => x.ContestId).ToArray();
        return new QuinaBlobDocument(list);
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
