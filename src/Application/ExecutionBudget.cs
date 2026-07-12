namespace Lotofacil.Loader.Application;

public sealed class ExecutionBudget : IExecutionBudget
{
    private readonly IClock _clock;

    public ExecutionBudget(IClock clock, DateTimeOffset deadlineUtc)
    {
        _clock = clock;
        DeadlineUtc = deadlineUtc;
    }

    public DateTimeOffset DeadlineUtc { get; }

    public TimeSpan Remaining
    {
        get
        {
            var remaining = DeadlineUtc - _clock.UtcNow;
            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }
    }

    public bool HasMinimumBudget(TimeSpan minimum) => Remaining >= minimum;

    public TimeSpan CapWait(TimeSpan requested)
    {
        if (requested <= TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        var remaining = Remaining;
        return remaining <= TimeSpan.Zero
            ? TimeSpan.Zero
            : requested <= remaining ? requested : remaining;
    }
}
