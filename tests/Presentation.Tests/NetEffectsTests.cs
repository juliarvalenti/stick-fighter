using System.Collections.Generic;
using System.Linq;
using StickFighter.FX;
using StickFighter.Sim;
using StickFighter.Sim.Net;
using StickFighter.Sim.Numerics;
using Xunit;

namespace StickFighter.Presentation.Tests;

// Effects over a real rollback session: the effect ledger (#46) already reconciles rollbacks, and
// this checks it holds up against the rollbacks a network causes. A peer presenting its state after
// every tick ends up having played exactly the effects an offline run of the same match plays: any
// started on a wrong prediction were cancelled when it was rolled back.
public class NetEffectsTests
{
    private const int Frames = 900;
    private const int Delay = 1;

    private static FrameInput[] Script()
    {
        var rng = Rng.Create(5, 11);
        Buttons[] pick = { Buttons.None, Buttons.Right, Buttons.Left, Buttons.LightPunch, Buttons.HeavyPunch, Buttons.Down | Buttons.MediumKick };
        var script = new FrameInput[Frames];
        Buttons p1 = Buttons.Right, p2 = Buttons.Left;
        for (var f = 0; f < Frames; f++)
        {
            if (rng.Chance(1, 5))
                p1 = pick[rng.NextInt(pick.Length)];
            if (rng.Chance(1, 5))
                p2 = pick[rng.NextInt(pick.Length)];
            script[f] = new FrameInput(p1, p2);
        }
        return script;
    }

    [Fact]
    public void APeerPlaysTheEffectsAnOfflineRunPlays()
    {
        var script = Script();
        var network = new SimulatedNetwork(3, 200, 10, 2, 5);
        var a = new RollbackSession(0, 1, Stage.Training, MatchMode.Sandbox, Delay, network.Ends[0]);
        var b = new RollbackSession(1, 1, Stage.Training, MatchMode.Sandbox, Delay, network.Ends[1]);
        var ledger = new EffectLedger();
        var played = new List<EffectLedger.Key>();

        for (var tick = 0; tick < 4 * Frames && !(a.ConfirmedFrame == Frames && b.ConfirmedFrame == Frames); tick++)
        {
            foreach (var s in new[] { a, b })
            {
                var frame = s.Frame + Delay;
                if (s.Frame < Frames)
                    s.Tick(frame < Frames ? script[frame].For(s.LocalPlayer) : Buttons.None);
                else
                    s.Poll();
            }
            network.Tick();

            ledger.Update(a.State);
            played.AddRange(ledger.Started.Select(e => EffectLedger.KeyOf(e)));
            foreach (var gone in ledger.Cancelled)
                Assert.True(played.Remove(EffectLedger.KeyOf(gone)), "cancelled only what was playing");
        }

        // Offline: the same inputs, every frame once.
        var state = GameState.NewMatch(1);
        var offlineLedger = new EffectLedger();
        var offline = new List<EffectLedger.Key>();
        for (var f = 0; f < Frames; f++)
        {
            Simulation.Step(ref state, f < Delay ? default : script[f]);
            offlineLedger.Update(state);
            offline.AddRange(offlineLedger.Started.Select(e => EffectLedger.KeyOf(e)));
            Assert.Empty(offlineLedger.Cancelled);
        }

        Assert.Equal(state.Checksum(), a.State.Checksum());
        Assert.True(a.Rollbacks > 0, "the network caused rollbacks");
        Assert.NotEmpty(offline);
        Assert.Equal(offline.OrderBy(k => k.Frame).ThenBy(k => k.Kind), played.OrderBy(k => k.Frame).ThenBy(k => k.Kind));
    }
}
