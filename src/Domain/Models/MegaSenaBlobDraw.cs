using System.Text.Json.Serialization;

namespace Lotofacil.Loader.Domain;

public sealed record MegaSenaBlobDraw(
    [property: JsonPropertyName("contest_id")] int ContestId,
    [property: JsonPropertyName("draw_date")] string DrawDate,
    [property: JsonPropertyName("numbers")] IReadOnlyList<int> Numbers,
    [property: JsonPropertyName("winners_6")] int Winners6,
    [property: JsonPropertyName("has_winner_6")] bool HasWinner6
);
