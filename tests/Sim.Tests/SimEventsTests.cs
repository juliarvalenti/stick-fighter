using System.Collections.Generic;
using StickFighter.Sim.Numerics;
using Xunit;

namespace StickFighter.Sim.Tests;

public class SimEventsTests
{
    private static GameState Apart(int gap)
    {
        var state = GameState.NewMatch(1);
        state.Fighters[0].Position = new FixVec2(Fix.FromInt(1000), state.Fighters[0].Position.Y);
        state.Fighters[1].Position = new FixVec2(Fix.FromInt(1000 + gap), state.Fighters[1].Position.Y);
        return state;
    }

    // Plays `frames` frames and returns every event recorded, in order.
    private static List<SimEvent> Events(GameState state, int frames, System.Func<int, FrameInput> input)
    {
        var events = new List<SimEvent>();
        for (var f = 0; f < frames; f++)
        {
            var next = state.NextEvent;
            Simulation.Step(ref state, input(f));
            for (var slot = next; slot != state.NextEvent; slot = (slot + 1) % SimEventRing.Capacity)
                events.Add(state.Events[slot]);
        }
        return events;
    }

    [Fact]
    public void AHitRecordsWhenWhoWhatAndWhere()
    {
        var events = Events(Apart(60), 20, f => new FrameInput(f == 0 ? Buttons.LightPunch : default, default));
        var hit = Assert.Single(events);
        Assert.Equal(EventKind.Hit, hit.Kind);
        Assert.Equal(Characters.Characters.Placeholder.FindMove("5LP")!.FirstActiveFrame, hit.Frame);
        Assert.Equal((0, 1), (hit.Attacker, hit.Defender));
        Assert.Equal(Characters.Characters.Placeholder.Moves.IndexOf(Characters.Characters.Placeholder.FindMove("5LP")!), hit.Move);
        // Where the fist met P2's body: past P1, around P2 (whose feet are at 1060), and off the floor.
        Assert.True(hit.Point.X > Fix.FromInt(1000) && hit.Point.X < Fix.FromInt(1120), $"x {hit.Point.X}");
        Assert.True(hit.Point.Y < Stage.Training.Floor, $"y {hit.Point.Y}");
    }

    [Fact]
    public void ABlockAndACounterHitAreTheirOwnKinds()
    {
        var block = Events(Apart(60), 20, f => new FrameInput(f == 0 ? Buttons.LightPunch : default, Buttons.Right));
        Assert.Equal(EventKind.Block, Assert.Single(block).Kind);

        var counter = Events(Apart(60), 20, f => new FrameInput(f == 0 ? Buttons.LightPunch : default, f == 0 ? Buttons.HeavyKick : default));
        Assert.Equal(EventKind.CounterHit, Assert.Single(counter).Kind);
    }

    [Fact]
    public void AThrowRecordsTheThrowAndTheKnockdown()
    {
        var events = Events(Apart(60), 20, f => new FrameInput(f == 0 ? Buttons.LightPunch | Buttons.LightKick : default, default));
        Assert.Equal(new[] { EventKind.Throw, EventKind.Knockdown }, events.ConvertAll(e => e.Kind));
    }

    [Fact]
    public void AKillingHitRecordsTheKo()
    {
        var state = Apart(60);
        state.Fighters[1].Health = 5;
        var events = Events(state, 20, f => new FrameInput(f == 0 ? Buttons.LightPunch : default, default));
        Assert.Equal(new[] { EventKind.Hit, EventKind.Ko }, events.ConvertAll(e => e.Kind));
    }

    [Fact]
    public void TheRingKeepsTheLatestEvents()
    {
        var state = GameState.NewMatch(1);
        for (var i = 0; i < SimEventRing.Capacity + 3; i++)
        {
            state.Frame = i;
            SimEvents.Add(ref state, EventKind.Hit, 0, 1, i, default);
        }
        var moves = new List<int>();
        for (var slot = 0; slot < SimEventRing.Capacity; slot++)
            moves.Add(state.Events[slot].Move);
        moves.Sort();
        Assert.Equal(3, moves[0]); // the three oldest were overwritten
        Assert.Equal(SimEventRing.Capacity + 2, moves[^1]);
    }

    // The ring has to cover the rollback window even in a busy frame: two events a frame (a trade, or
    // a hit and a KO) for every frame of the window still fit.
    [Fact]
    public void TheRingCoversTheWindowAtTwoEventsAFrame()
    {
        Assert.True(SimEventRing.Capacity >= 2 * SimEventRing.Window);
        Assert.True(SimEventRing.Window >= Replays.SyncTest.MaxRollback);
        Assert.True(SimEventRing.Window >= TickDriver.MaxTicksPerUpdate);
    }
}
