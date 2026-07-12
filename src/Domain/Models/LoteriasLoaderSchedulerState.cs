namespace Lotofacil.Loader.Domain;

public sealed record LoteriasLoaderSchedulerState(
    int NextModalityIndex,
    string? LastModalityKey,
    DateTimeOffset? LastRunUtc,
    string? ETag);
