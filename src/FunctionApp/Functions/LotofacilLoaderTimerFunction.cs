using Lotofacil.Loader.Application;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Lotofacil.Loader.FunctionApp.Functions;

public sealed class LotofacilLoaderTimerFunction
{
    private readonly ILogger<LotofacilLoaderTimerFunction> _log;
    private readonly V0EnvironmentValidator _validator;
    private readonly UpdateLotofacilResultsUseCase _useCase;

    public LotofacilLoaderTimerFunction(
        ILogger<LotofacilLoaderTimerFunction> log,
        V0EnvironmentValidator validator,
        UpdateLotofacilResultsUseCase useCase)
    {
        _log = log;
        _validator = validator;
        _useCase = useCase;
    }

    [Function(nameof(LotofacilLoaderTimerFunction))]
    public async Task RunAsync(
        [TimerTrigger("0 0 * * * *")] TimerInfo timer,
        CancellationToken ct)
    {
        // Trigger fino: valida config + chama caso de uso (sem duplicar semântica).
        var runId = Guid.NewGuid().ToString("n");

        var validation = _validator.Validate();
        if (!validation.IsValid)
        {
            _log.LogError(
                "v0_stop reason_stop={reason_stop} run_id={run_id} error={error}",
                ReasonStop.HARD_FAIL_CONFIG_INVALID,
                runId,
                validation.Error
            );
            return;
        }

        UpdateLotofacilResultsOutcome outcome;
        try
        {
            outcome = await _useCase.ExecuteAsync(ct);
        }
        catch (Exception ex)
        {
            // Handler permanece fino: não classifica aqui (isso evolui por fatia no núcleo).
            _log.LogError(ex, "v0_unhandled run_id={run_id}", runId);
            throw;
        }

        _log.LogInformation(
            "v0_stop reason_stop={reason_stop} run_id={run_id} deadline_seconds={deadline_seconds} timezone={timezone} last_loaded_contest_id={last_loaded_contest_id} latest_id={latest_id} processed_count={processed_count} persisted_last_id={persisted_last_id}",
            outcome.ReasonStop,
            runId,
            outcome.DeadlineSeconds,
            outcome.Timezone,
            outcome.LastLoadedContestId,
            outcome.LatestId,
            outcome.ProcessedCount,
            outcome.PersistedLastId
        );
    }
}

