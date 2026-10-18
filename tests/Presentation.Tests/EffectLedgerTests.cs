using System.Collections.Generic;
using StickFighter.FX;
using StickFighter.Sim;
using StickFighter.Sim.Numerics;
using Xunit;

namespace StickFighter.Presentation.Tests;

// The rollback rules for effects: each plays exactly once however often its frame is simulated, and
// one whose cause a rollback removes is cancelled.
public class EffectLedgerTests
{
    private static GameState Apart(int gap)
    {
        var state = GameState.NewMatch(1);
        state.Fighters[0].Position = new FixVec2(Fix.FromInt(1000), state.Fighters[0].Position.Y);
        state.Fighters[1].Position = new FixVec2(Fix.FromInt(1000 + gap), state.Fighters[1].Position.Y);
        return state;
    }

    // P1's heavy punch, pressed on frame 0, hits on frame 7.
    private static FrameInput Script(int frame) => new(frame == 0 ? Buttons.HeavyPunch : default, default);

    private sealed class Run
    {
        public GameState State = Apart(60);
        public readonly EffectLedger Ledger = new();
        public readonly StateBuffer Saved = new(16);
        public readonly List<SimEvent> Started = new();
        public readonly List<SimEvent> Cancelled = new();

        public void Step(FrameInput input)
        {
            Saved.Save(State);
            Simulation.Step(ref State, input);
            Ledger.Update(State);
            Started.AddRange(Ledger.Started);
            Cancelled.AddRange(Ledger.Cancelled);
        }

        public void RollBack(int frames) => Assert.True(Saved.TryLoad(State.Frame - frames, ref State));
    }

    [Fact]
    public void AHitPlaysOnceWithItsShake()
    {
        var run = new Run();
        for (var f = 0; f < 30; f++)
            run.Step(Script(f));
        var hit = Assert.Single(run.Started);
        Assert.Equal(EventKind.Hit, hit.Kind);
        Assert.True(EffectLedger.ShakeFor(hit).Frames > 0, "a heavy hit shakes the screen");
        Assert.Empty(run.Cancelled);
    }

    // The spec's test: roll back 4 frames after the hit and resimulate the same inputs. The spark and
    // the shake have already played; nothing starts again and nothing is cancelled.
    [Fact]
    public void ResimulatingTheSameInputsDoesntReplayIt()
    {
        var run = new Run();
        for (var f = 0; f < 10; f++)
            run.Step(Script(f));
        Assert.Single(run.Started);

        run.RollBack(4); // back to frame 6, before the hit on 7
        for (var f = run.State.Frame; f < 30; f++)
            run.Step(Script(f));

        Assert.Single(run.Started);
        Assert.Empty(run.Cancelled);
        Assert.Contains(run.Ledger.Playing, e => e.Kind == EventKind.Hit);
    }

    // Roll back to before the press and resimulate without it: the hit never happens, so its spark
    // (and shake) is cancelled and no longer playing.
    [Fact]
    public void ADifferentInputAfterTheRollbackCancelsIt()
    {
        var run = new Run();
        for (var f = 0; f < 9; f++)
            run.Step(Script(f));
        var hit = Assert.Single(run.Started);

        run.RollBack(9); // back to frame 0: the punch is never pressed
        for (var f = run.State.Frame; f < 20; f++)
            run.Step(default);

        Assert.Equal(EffectLedger.KeyOf(hit), EffectLedger.KeyOf(Assert.Single(run.Cancelled)));
        Assert.DoesNotContain(run.Ledger.Playing, e => e.Kind == EventKind.Hit);
    }

    // The game updates the presentation once per display frame, which can run several ticks. The
    // event is still seen, once.
    [Fact]
    public void SeveralTicksBetweenUpdatesStillPlayItOnce()
    {
        var state = Apart(60);
        var ledger = new EffectLedger();
        var started = 0;
        for (var f = 0; f < 30; f++)
        {
            Simulation.Step(ref state, Script(f));
            if (f % TickDriver.MaxTicksPerUpdate == TickDriver.MaxTicksPerUpdate - 1)
            {
                ledger.Update(state);
                started += ledger.Started.Count;
            }
        }
        Assert.Equal(1, started);
    }

    [Fact]
    public void AnEffectIsDroppedAfterItsLifetime()
    {
        var run = new Run();
        for (var f = 0; f < 10; f++)
            run.Step(Script(f));
        Assert.NotEmpty(run.Ledger.Playing);
        for (var f = 10; f < 10 + EffectLedger.Lifetime + 5; f++)
            run.Step(default);
        Assert.Empty(run.Ledger.Playing);
    }
}
