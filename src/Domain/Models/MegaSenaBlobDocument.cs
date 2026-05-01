using System.Text.Json.Serialization;

namespace Lotofacil.Loader.Domain;

public sealed record MegaSenaBlobDocument(
    [property: JsonPropertyName("draws")] IReadOnlyList<MegaSenaBlobDraw> Draws
);
