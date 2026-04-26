namespace Lotofacil.Loader.Infrastructure;

public sealed class LotodicasOptions
{
    public const string SectionName = "Lotodicas";

    public required string BaseUrl { get; init; }
    public required string Token { get; init; }
}

