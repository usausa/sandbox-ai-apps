namespace AutoOrder.Domain.Tests;

using Shouldly;

public sealed class DomainSmokeTests
{
    [Fact]
    public void PlaceholderPasses()
    {
        var result = 1 + 1;
        result.ShouldBe(2);
    }
}
