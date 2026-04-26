namespace Lotofacil.Loader.Application;

public interface IDelay
{
    Task DelayAsync(TimeSpan delay, CancellationToken ct);
}

