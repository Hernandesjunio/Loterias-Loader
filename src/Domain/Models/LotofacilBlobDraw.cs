using System.Text.Json.Serialization;

namespace Lotofacil.Loader.Domain;

public sealed record LotofacilBlobDraw(
    [property: JsonPropertyName("contest_id")] int ContestId,
    [property: JsonPropertyName("draw_date")] string DrawDate,
    [property: JsonPropertyName("numbers")] IReadOnlyList<int> Numbers,
    [property: JsonPropertyName("winners_15")] int Winners15,
    [property: JsonPropertyName("has_winner_15")] bool HasWinner15
);

