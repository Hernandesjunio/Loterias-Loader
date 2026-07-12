using Lotofacil.Loader.Application;
using Xunit;

namespace ContractTests.V0;

public sealed class ExecutionBudgetTests
{
    [Fact]
    public void H1_cap_wait_returns_remaining_when_retry_exceeds_budget()
    {
        var t0 = ContractTestHarness.Utc("2026-07-12T20:00:00Z");
        var clock = new FakeClock(t0);
        var budget = new ExecutionBudget(clock, t0.AddSeconds(20));

        var capped = budget.CapWait(TimeSpan.FromSeconds(60));

        Assert.Equal(TimeSpan.FromSeconds(20), capped);
    }

    [Fact]
    public void H1_cap_wait_returns_zero_when_budget_exhausted()
    {
        var t0 = ContractTestHarness.Utc("2026-07-12T20:00:00Z");
        var clock = new FakeClock(t0);
        var budget = new ExecutionBudget(clock, t0);

        Assert.Equal(TimeSpan.Zero, budget.CapWait(TimeSpan.FromSeconds(60)));
        Assert.False(budget.HasMinimumBudget(TimeSpan.FromSeconds(15)));
    }

    [Fact]
    public void H1_cap_wait_returns_requested_when_budget_is_sufficient()
    {
        var t0 = ContractTestHarness.Utc("2026-07-12T20:00:00Z");
        var clock = new FakeClock(t0);
        var budget = new ExecutionBudget(clock, t0.AddSeconds(180));

        Assert.Equal(TimeSpan.FromSeconds(30), budget.CapWait(TimeSpan.FromSeconds(30)));
    }
}
