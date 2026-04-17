using FluentAssertions;
using Server.Lifecycle;
using Xunit;

namespace Server.Tests;

public class ReadinessGateTests
{
    [Fact]
    public void StartsReady()
    {
        new ReadinessGate().IsReady.Should().BeTrue();
    }

    [Fact]
    public void MarkNotReady_FlipsFlag()
    {
        var g = new ReadinessGate();
        g.MarkNotReady();
        g.IsReady.Should().BeFalse();
    }
}
