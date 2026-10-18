using System;
using Xunit;

namespace StickFighter.Sim.Tests;

public class AllocationTests
{
    [Fact]
    public void StepAndSaveDoNotAllocate()
    {
        var state = new GameState();
        var saved = new GameState[8];

        // Warm up so JIT and static initialisation don't count.
        for (var i = 0; i < 1_000; i++)
            Simulation.Step(ref state);

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10_000; i++)
        {
            saved[i % saved.Length] = state;
            Simulation.Step(ref state);
        }
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
    }
}
