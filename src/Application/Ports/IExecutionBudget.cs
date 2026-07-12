namespace Lotofacil.Loader.Application;

public interface IExecutionBudget
{
    DateTimeOffset DeadlineUtc { get; }

    TimeSpan Remaining { get; }

    bool HasMinimumBudget(TimeSpan minimum);

    TimeSpan CapWait(TimeSpan requested);
}
