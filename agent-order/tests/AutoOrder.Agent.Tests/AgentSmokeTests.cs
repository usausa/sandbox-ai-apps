namespace AutoOrder.Agent.Tests;

using Shouldly;

public sealed class AgentSmokeTests
{
    [Fact]
    public void PlaceholderPasses()
    {
        var result = 1 + 1;
        result.ShouldBe(2);
    }
}
