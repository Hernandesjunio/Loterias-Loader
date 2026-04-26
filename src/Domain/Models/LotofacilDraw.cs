namespace Lotofacil.Loader.Domain;

public sealed record LotofacilDraw(
    int ContestId,
    string DrawDate,
    IReadOnlyList<int> Numbers,
    int Winners15
)
{
    public bool HasWinner15 => Winners15 > 0;
}

