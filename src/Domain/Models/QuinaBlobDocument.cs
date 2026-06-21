using System.Text.Json.Serialization;

namespace Lotofacil.Loader.Domain;

public sealed record QuinaBlobDocument(
    [property: JsonPropertyName("draws")] IReadOnlyList<QuinaBlobDraw> Draws
);
