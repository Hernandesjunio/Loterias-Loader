namespace Lotofacil.Loader.Application;

public interface ILotofacilApiClient
{
    // Placeholder signatures (ports): adaptadores reais virão em Infrastructure.
    Task<int> GetLatestContestIdAsync(CancellationToken ct);
    Task<object> GetContestByIdRawAsync(int contestId, CancellationToken ct);
}

