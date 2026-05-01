namespace Lotofacil.Loader.Application;

/// <summary>
/// Estratégia por modalidade: formato do blob JSON e parse da resposta da API por concurso.
/// </summary>
public interface ILoteriaBlobCatalog
{
    object EmptyDocument();

    object ParseDocument(object raw);

    object ParseContestToDraw(object rawContest);

    int GetContestIdFromDraw(object draw);

    string? GetDrawDateFromDraw(object draw);

    object MergeOrderedDraws(IReadOnlyDictionary<int, object> drawsByContestId);
}
