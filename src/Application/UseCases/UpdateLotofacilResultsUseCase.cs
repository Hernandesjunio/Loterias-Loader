namespace Lotofacil.Loader.Application;

public sealed class UpdateLotofacilResultsUseCase
{
    private readonly IClock _clock;
    private readonly ILotofacilApiClient _api;
    private readonly ILotofacilBlobStore _blob;
    private readonly ILotofacilStateStore _state;

    public UpdateLotofacilResultsUseCase(
        IClock clock,
        ILotofacilApiClient api,
        ILotofacilBlobStore blob,
        ILotofacilStateStore state)
    {
        _clock = clock;
        _api = api;
        _blob = blob;
        _state = state;
    }

    public Task ExecuteAsync(CancellationToken ct) =>
        throw new NotImplementedException(
            "V0 ainda não implementada. Referência: docs/spec-driven-execution-guide.md (Contrato V0 — normativo)."
        );
}

