using StickFighter.Sim.Numerics;
using Xunit;

namespace StickFighter.Sim.Tests;

public class RngTests
{
    // From the reference pcg32-demo: pcg32_srandom_r(&rng, 42, 54).
    [Fact]
    public void MatchesReferenceOutput()
    {
        var rng = Rng.Create(42, 54);
        uint[] expected = { 0xa15c02b7, 0x7b47f409, 0xba1d3330, 0x83d2f293, 0xbfa4784b, 0xcbed606e };
        foreach (var value in expected)
            Assert.Equal(value, rng.NextUInt());
    }

    [Fact]
    public void CopiesReplayTheSameSequence()
    {
        var a = Rng.Create(7);
        a.NextUInt();
        var saved = a;
        var first = new[] { a.NextInt(100), a.NextInt(100), a.NextInt(100) };
        var replay = new[] { saved.NextInt(100), saved.NextInt(100), saved.NextInt(100) };
        Assert.Equal(first, replay);
    }

    [Fact]
    public void BoundedValuesStayInRange()
    {
        var rng = Rng.Create(1);
        var seen = new bool[6];
        for (var i = 0; i < 1000; i++)
        {
            var roll = rng.NextInt(1, 6);
            Assert.InRange(roll, 1, 6);
            seen[roll - 1] = true;
        }
        Assert.All(seen, Assert.True);
    }

    [Fact]
    public void DifferentSeedsDiffer()
    {
        Assert.NotEqual(Rng.Create(1).NextUInt(), Rng.Create(2).NextUInt());
    }
}
