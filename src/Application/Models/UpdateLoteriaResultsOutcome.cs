namespace Lotofacil.Loader.Application;

public sealed record UpdateLoteriaResultsOutcome(
    string ModalityKey,
    ReasonStop ReasonStop,
    int LastLoadedContestId,
    int? LatestId,
    int ProcessedCount,
    int PersistedLastId,
    int DeadlineSeconds,
    string Timezone
);
