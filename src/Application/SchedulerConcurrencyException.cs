namespace Lotofacil.Loader.Application;

public sealed class SchedulerConcurrencyException : Exception
{
    public SchedulerConcurrencyException(string message) : base(message)
    {
    }

    public SchedulerConcurrencyException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
