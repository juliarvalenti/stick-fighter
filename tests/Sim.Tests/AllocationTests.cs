using System;
using Xunit;

namespace StickFighter.Sim.Tests;

public class AllocationTests
{
    [Fact]
    public void StepSaveLoadAndChecksumDoNotAllocate()
    {
        var state = GameState.NewMatch(1);
        var buffer = new StateBuffer(8);
        var input = new FrameInput(FrameInput.Right, FrameInput.Left);
        ulong checksum = 0;

        // Warm up so JIT and static initialisation don't count.
        for (var i = 0; i < 1_000; i++)
        {
            buffer.Save(state);
            Simulation.Step(ref state, input);
            checksum ^= state.Checksum();
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10_000; i++)
        {
            buffer.Save(state);
            Simulation.Step(ref state, input);
            checksum ^= state.Checksum();
            if (i % 8 == 7)
                Assert.True(buffer.TryLoad(state.Frame - 4, ref state));
        }
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
    }
}
