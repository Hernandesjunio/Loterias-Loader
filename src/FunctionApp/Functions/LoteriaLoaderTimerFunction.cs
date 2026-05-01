using Lotofacil.Loader.Application;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Lotofacil.Loader.FunctionApp.Functions;

public sealed class LoteriaLoaderTimerFunction
{
    private readonly ILogger<LoteriaLoaderTimerFunction> _log;
    private readonly V0EnvironmentValidator _validator;
    private readonly LoteriaResultsUpdateUseCase _lotofacil;
    private readonly LoteriaResultsUpdateUseCase _megaSena;

    public LoteriaLoaderTimerFunction(
        ILogger<LoteriaLoaderTimerFunction> log,
        V0EnvironmentValidator validator,
        [FromKeyedServices(LoteriaModalityKeys.Lotofacil)] LoteriaResultsUpdateUseCase lotofacil,
        [FromKeyedServices(LoteriaModalityKeys.MegaSena)] LoteriaResultsUpdateUseCase megaSena)
    {
        _log = log;
        _validator = validator;
        _lotofacil = lotofacil;
        _megaSena = megaSena;
    }

    [Function(nameof(LoteriaLoaderTimerFunction))]
    public async Task RunAsync(
        [TimerTrigger("%LoteriasLoader__TimerSchedule%")] TimerInfo timer,
        CancellationToken ct)
    {
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

        foreach (var useCase in new[] { _lotofacil, _megaSena })
        {
            try
            {
                var outcome = await useCase.ExecuteAsync(ct);
                _log.LogInformation(
                    "v0_stop reason_stop={reason_stop} run_id={run_id} modality={modality} deadline_seconds={deadline_seconds} timezone={timezone} last_loaded_contest_id={last_loaded_contest_id} latest_id={latest_id} processed_count={processed_count} persisted_last_id={persisted_last_id}",
                    outcome.ReasonStop,
                    runId,
                    outcome.ModalityKey,
                    outcome.DeadlineSeconds,
                    outcome.Timezone,
                    outcome.LastLoadedContestId,
                    outcome.LatestId,
                    outcome.ProcessedCount,
                    outcome.PersistedLastId
                );
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "v0_unhandled run_id={run_id} modality={modality}", runId, useCase.ModalityKey);
            }
        }
    }
}
