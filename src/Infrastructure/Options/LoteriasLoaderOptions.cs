using Lotofacil.Loader.Application;

namespace Lotofacil.Loader.Infrastructure;

public sealed class LoteriasLoaderOptions
{
    public const string SectionName = "LoteriasLoader";

    public bool SequentialAllModalities { get; init; }

    public string ModalityOrder { get; init; } =
        $"{LoteriaModalityKeys.Lotofacil},{LoteriaModalityKeys.MegaSena},{LoteriaModalityKeys.Quina}";

    public IReadOnlyList<string> ParseModalityOrder()
    {
        if (string.IsNullOrWhiteSpace(ModalityOrder))
        {
            return DefaultModalityOrder();
        }

        var parts = ModalityOrder
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return parts.Length == 0 ? DefaultModalityOrder() : parts;
    }

    private static IReadOnlyList<string> DefaultModalityOrder() =>
        new[]
        {
            LoteriaModalityKeys.Lotofacil,
            LoteriaModalityKeys.MegaSena,
            LoteriaModalityKeys.Quina
        };
}
