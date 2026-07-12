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
    private readonly IRunContext _runContext;
    private readonly ILoteriasLoaderScheduler _scheduler;
    private readonly LoteriasLoaderOptions _loaderOptions;
    private readonly IReadOnlyDictionary<string, LoteriaResultsUpdateUseCase> _useCasesByModality;

    public LoteriaLoaderTimerFunction(
        ILogger<LoteriaLoaderTimerFunction> log,
        V0EnvironmentValidator validator,
        IRunContext runContext,
        ILoteriasLoaderScheduler scheduler,
        IOptions<LoteriasLoaderOptions> loaderOptions,
        [FromKeyedServices(LoteriaModalityKeys.Lotofacil)] LoteriaResultsUpdateUseCase lotofacil,
        [FromKeyedServices(LoteriaModalityKeys.MegaSena)] LoteriaResultsUpdateUseCase megaSena,
        [FromKeyedServices(LoteriaModalityKeys.Quina)] LoteriaResultsUpdateUseCase quina)
    {
        _log = log;
        _validator = validator;
        _runContext = runContext;
        _scheduler = scheduler;
        _loaderOptions = loaderOptions.Value;
        _useCasesByModality = new Dictionary<string, LoteriaResultsUpdateUseCase>(StringComparer.Ordinal)
        {
            [LoteriaModalityKeys.Lotofacil] = lotofacil,
            [LoteriaModalityKeys.MegaSena] = megaSena,
            [LoteriaModalityKeys.Quina] = quina
        };
    }

    [Function(nameof(LoteriaLoaderTimerFunction))]
    public async Task RunAsync(
        [TimerTrigger("%LoteriasLoader:TimerSchedule%")] TimerInfo timer,
        CancellationToken ct)
    {
        var runId = Guid.NewGuid().ToString("n");

        _log.LogDebug("v0_run.start run_id={run_id}", runId);

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

        if (_loaderOptions.SequentialAllModalities)
        {
            await RunSequentialAllModalitiesAsync(runId, ct);
            return;
        }

        await RunRotatedModalityAsync(runId, ct);
    }

    private async Task RunRotatedModalityAsync(string runId, CancellationToken ct)
    {
        var acquire = await _scheduler.AcquireNextModalityAsync(ct);

        _log.LogInformation(
            "v0_scheduler modality={modality} index={index} run_id={run_id}",
            acquire.ModalityKey,
            acquire.Index,
            runId);

        try
        {
            await ExecuteModalityAsync(runId, acquire.ModalityKey, ct);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "v0_unhandled run_id={run_id} modality={modality}", runId, acquire.ModalityKey);
        }
        finally
        {
            await _scheduler.AdvanceAfterAttemptAsync(acquire, ct);
        }
    }

    private async Task RunSequentialAllModalitiesAsync(string runId, CancellationToken ct)
    {
        foreach (var modalityKey in _loaderOptions.ParseModalityOrder())
        {
            if (!_useCasesByModality.ContainsKey(modalityKey))
            {
                _log.LogError(
                    "v0_stop reason_stop={reason_stop} run_id={run_id} modality={modality} error={error}",
                    ReasonStop.HARD_FAIL_CONFIG_INVALID,
                    runId,
                    modalityKey,
                    "Modalidade configurada sem use case registrado.");
                continue;
            }

            try
            {
                await ExecuteModalityAsync(runId, modalityKey, ct);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "v0_unhandled run_id={run_id} modality={modality}", runId, modalityKey);
            }
        }
    }

    private async Task ExecuteModalityAsync(string runId, string modalityKey, CancellationToken ct)
    {
        if (!_useCasesByModality.TryGetValue(modalityKey, out var useCase))
        {
            throw new InvalidOperationException($"Use case not registered for modality '{modalityKey}'.");
        }

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
}
