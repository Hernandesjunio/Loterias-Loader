namespace Lotofacil.Loader.Application;

/// <summary>
/// Cliente HTTP para resultados Lotodicas v2. O segmento identifica a modalidade (ex.: lotofacil, mega_sena).
/// </summary>
public interface ILotteriesApiClient
{
    Task<object> GetAllResultsRawAsync(string lotteryApiSegment, CancellationToken ct);
}
