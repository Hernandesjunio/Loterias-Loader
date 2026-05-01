using System.Text.Json.Serialization;

namespace Lotofacil.Loader.Domain;

public sealed record LoteriaLoaderState(
    [property: JsonPropertyName("last_loaded_contest_id")] int LastLoadedContestId,
    [property: JsonPropertyName("last_loaded_draw_date")] string? LastLoadedDrawDate,
    [property: JsonPropertyName("last_updated_at_utc")] DateTimeOffset LastUpdatedAtUtc,
    [property: JsonPropertyName("etag")] string? ETag
);
