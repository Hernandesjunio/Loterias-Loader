using System.Text.Json.Serialization;

namespace Lotofacil.Loader.Domain;

public sealed record LotofacilBlobDocument(
    [property: JsonPropertyName("draws")] IReadOnlyList<LotofacilBlobDraw> Draws
);

