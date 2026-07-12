namespace Lotofacil.Loader.Application;

public interface ILoteriasLoaderScheduler
{
    Task<LoteriasSchedulerAcquireResult> AcquireNextModalityAsync(CancellationToken ct);

    Task AdvanceAfterAttemptAsync(LoteriasSchedulerAcquireResult acquire, CancellationToken ct);
}
