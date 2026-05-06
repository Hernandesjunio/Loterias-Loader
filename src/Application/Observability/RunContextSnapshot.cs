namespace Lotofacil.Loader.Application;

public sealed record RunContextSnapshot(
    string RunId,
    string Modality,
    int RetriesCount,
    double RateLimitWaitSecondsTotal);

