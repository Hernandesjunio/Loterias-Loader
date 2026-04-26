namespace Lotofacil.Loader.Application;

public sealed record UpdateLotofacilResultsOutcome(
    ReasonStop ReasonStop,
    int LastLoadedContestId,
    int? LatestId,
    int ProcessedCount,
    int PersistedLastId,
    int DeadlineSeconds,
    string Timezone
);

