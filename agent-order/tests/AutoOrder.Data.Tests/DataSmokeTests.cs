namespace AutoOrder.Data.Tests;

using Shouldly;

public sealed class DataSmokeTests
{
    [Fact]
    public void PlaceholderPasses()
    {
        var result = 1 + 1;
        result.ShouldBe(2);
    }
}
