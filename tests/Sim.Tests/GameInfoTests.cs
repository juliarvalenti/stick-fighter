using Xunit;

namespace StickFighter.Sim.Tests;

public class GameInfoTests
{
    [Fact]
    public void SimulationRunsAtSixtyTicks()
    {
        Assert.Equal(60, GameInfo.TicksPerSecond);
    }
}
