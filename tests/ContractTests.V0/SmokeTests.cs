using Xunit;

namespace ContractTests.V0;

public sealed class SmokeTests
{
    [Fact]
    public void TestRunner_is_working()
    {
        Assert.True(true);
    }

    [Fact]
    public void V0_entrypoint_is_discoverable()
    {
        _ = SutContract.RequireEntryPointType();
    }
}

