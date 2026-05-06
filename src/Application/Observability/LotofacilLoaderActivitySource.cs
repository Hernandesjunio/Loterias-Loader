using System.Diagnostics;

namespace Lotofacil.Loader.Application;

public static class LotofacilLoaderActivitySource
{
    public const string Name = "Lotofacil.Loader";

    public static readonly ActivitySource Instance = new(Name);
}

