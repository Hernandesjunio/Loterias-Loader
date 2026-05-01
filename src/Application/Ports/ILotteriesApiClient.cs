namespace Lotofacil.Loader.Application;

/// <summary>
/// Cliente HTTP para resultados Lotodicas v2. O segmento identifica a modalidade (ex.: lotofacil, mega_sena).
/// </summary>
public interface ILotteriesApiClient
{
    Task<int> GetLatestContestIdAsync(string lotteryApiSegment, CancellationToken ct);

    Task<object> GetContestByIdRawAsync(string lotteryApiSegment, int contestId, CancellationToken ct);

    Task<object> GetAllResultsRawAsync(string lotteryApiSegment, CancellationToken ct);
}
