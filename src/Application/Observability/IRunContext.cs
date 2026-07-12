namespace Lotofacil.Loader.Application;

public interface IRunContext
{
    RunContextSnapshot? Current { get; }

    IExecutionBudget? CurrentBudget { get; }

    IDisposable BeginRun(string runId, string modality);

    void SetExecutionBudget(IExecutionBudget? budget);

    void IncrementRetries(int count = 1);

    void AddWaitSeconds(double seconds);
}

