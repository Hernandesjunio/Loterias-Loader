using System.Text.Json.Serialization;

namespace Lotofacil.Loader.Domain;

public sealed record QuinaBlobDraw(
    [property: JsonPropertyName("contest_id")] int ContestId,
    [property: JsonPropertyName("draw_date")] string DrawDate,
    [property: JsonPropertyName("numbers")] IReadOnlyList<int> Numbers,
    [property: JsonPropertyName("winners_5")] int Winners5,
    [property: JsonPropertyName("has_winner_5")] bool HasWinner5
);
