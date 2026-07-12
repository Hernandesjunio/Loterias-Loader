using Lotofacil.Loader.Domain;

namespace Lotofacil.Loader.Application;

public sealed record LoteriasSchedulerAcquireResult(
    string ModalityKey,
    int Index,
    LoteriasLoaderSchedulerState State);
