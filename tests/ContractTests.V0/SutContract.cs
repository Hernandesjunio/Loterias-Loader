using Lotofacil.Loader.V0.Contract;
using Xunit;

namespace ContractTests.V0;

internal static class SutContract
{
    internal static Type RequireEntryPointType()
    {
        const string expectedTypeName = "Lotofacil.Loader.V0.Contract.EntryPoint";
        var t = typeof(EntryPoint);

        Assert.Equal(expectedTypeName, t.FullName);
        return t;
    }
}

