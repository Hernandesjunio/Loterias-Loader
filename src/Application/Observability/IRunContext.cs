namespace Lotofacil.Loader.Application;

public interface IRunContext
{
    RunContextSnapshot? Current { get; }

    IDisposable BeginRun(string runId, string modality);

    void IncrementRetries(int count = 1);

    void AddWaitSeconds(double seconds);
}

