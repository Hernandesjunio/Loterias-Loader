using Lotofacil.Loader.Application;
using Lotofacil.Loader.Infrastructure;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;

namespace Lotofacil.Loader.FunctionApp.Functions;

public sealed class LoteriaLoaderTimerFunction
{
    private readonly ILogger<LoteriaLoaderTimerFunction> _log;
    private readonly V0EnvironmentValidator _validator;
    private readonly CalendarGuardsTogglesOptions _calendarToggles;
    private readonly IRunContext _runContext;
    private readonly LoteriaResultsUpdateUseCase _lotofacil;
    private readonly LoteriaResultsUpdateUseCase _megaSena;

    public LoteriaLoaderTimerFunction(
        ILogger<LoteriaLoaderTimerFunction> log,
        V0EnvironmentValidator validator,
        IOptions<CalendarGuardsTogglesOptions> calendarToggles,
        IRunContext runContext,
        [FromKeyedServices(LoteriaModalityKeys.Lotofacil)] LoteriaResultsUpdateUseCase lotofacil,
        [FromKeyedServices(LoteriaModalityKeys.MegaSena)] LoteriaResultsUpdateUseCase megaSena)
    {
        _log = log;
        _validator = validator;
        _calendarToggles = calendarToggles.Value;
        _runContext = runContext;
        _lotofacil = lotofacil;
        _megaSena = megaSena;
    }

    [Function(nameof(LoteriaLoaderTimerFunction))]
    public async Task RunAsync(
        [TimerTrigger("%LoteriasLoader:TimerSchedule%")] TimerInfo timer,
        CancellationToken ct)
    {
        var runId = Guid.NewGuid().ToString("n");

        _log.LogDebug(
            "v0_run.start run_id={run_id} disable_business_day_guard={disable_business_day_guard} disable_20h_guard={disable_20h_guard}",
            runId,
            _calendarToggles.DisableBusinessDayGuard,
            _calendarToggles.Disable20hGuard);

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
                using var runScope = _runContext.BeginRun(runId, useCase.ModalityKey);
                using var scope = _log.BeginScope(new Dictionary<string, object?>
                {
                    ["run_id"] = runId,
                    ["modality"] = useCase.ModalityKey,
                    ["trace_id"] = Activity.Current?.TraceId.ToString()
                });

                _log.LogDebug("v0_run.modality.start");
                var outcome = await useCase.ExecuteAsync(ct);
                _log.LogDebug("v0_run.modality.stop reason_stop={reason_stop}", outcome.ReasonStop);

                _log.LogInformation(
                    "v0_stop reason_stop={reason_stop} run_id={run_id} modality={modality} deadline_seconds={deadline_seconds} timezone={timezone} disable_business_day_guard={disable_business_day_guard} disable_20h_guard={disable_20h_guard} last_loaded_contest_id={last_loaded_contest_id} latest_id={latest_id} processed_count={processed_count} persisted_last_id={persisted_last_id}",
                    outcome.ReasonStop,
                    runId,
                    outcome.ModalityKey,
                    outcome.DeadlineSeconds,
                    outcome.Timezone,
                    _calendarToggles.DisableBusinessDayGuard,
                    _calendarToggles.Disable20hGuard,
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
