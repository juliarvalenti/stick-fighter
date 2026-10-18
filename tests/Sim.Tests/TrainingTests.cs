using System;
using System.Collections.Generic;
using StickFighter.Sim.Characters;
using StickFighter.Sim.Collision;
using StickFighter.Sim.Numerics;
using StickFighter.Sim.Training;
using Xunit;

namespace StickFighter.Sim.Tests;

public class TrainingTests
{
    private static Character Data => Characters.Characters.Placeholder;

    // P1 walks in and throws `attacks` in turn from close range, each once both fighters are free
    // again, then stands. The dummy is P2.
    private static Func<GameState, Buttons> Attacking(params Buttons[] attacks)
    {
        var next = 0;
        return state =>
        {
            var me = state.Fighters[0];
            var them = state.Fighters[1];
            if (next >= attacks.Length)
                return Buttons.None;
            var free = me.Action is (int)FighterAction.Stand or (int)FighterAction.WalkForward or (int)FighterAction.Crouch;
            var themFree = them.Hitstun == 0 && them.Blockstun == 0;
            var close = Fix.Abs(them.Position.X - me.Position.X) <= Fix.FromInt(70);
            if (!(free && themFree))
                return Buttons.None;
            if (!close)
                return Buttons.Right;
            return attacks[next++];
        };
    }

    // What happened to P2 on each frame: hit (hitstun went up), blocked (blockstun went up).
    private sealed record Contact(int Frame, bool Blocked, bool Crouching);

    private static List<Contact> Contacts(Dummy dummy, Func<GameState, Buttons> p1, int frames, Action<GameState>? each = null,
        MatchMode mode = MatchMode.Training)
    {
        var state = GameState.NewMatch(1, Stage.Training, mode);
        var contacts = new List<Contact>();
        for (var f = 0; f < frames; f++)
        {
            var before = state.Fighters[1];
            Simulation.Step(ref state, new FrameInput(p1(state), dummy.Next(state, 1)));
            var after = state.Fighters[1];
            if (after.Hitstun > before.Hitstun || after.Blockstun > before.Blockstun)
                contacts.Add(new Contact(state.Frame, after.Blockstun > before.Blockstun, after.Action == (int)FighterAction.BlockCrouch));
            each?.Invoke(state);
        }
        return contacts;
    }

    private static Dummy Set(DummyAction action) => new() { Action = action };

    [Fact]
    public void AStandingDummyTakesEveryHit()
    {
        var contacts = Contacts(Set(DummyAction.Stand), Attacking(Buttons.MediumPunch, Buttons.MediumPunch), 200);
        Assert.Equal(2, contacts.Count);
        Assert.All(contacts, c => Assert.False(c.Blocked));
    }

    [Fact]
    public void ACrouchingDummyStaysDown()
    {
        var crouched = 0;
        Contacts(Set(DummyAction.Crouch), _ => Buttons.None, 60, s => crouched += s.Fighters[1].Action == (int)FighterAction.Crouch ? 1 : 0);
        Assert.True(crouched >= 60 - Data.Stats.CrouchTransitionFrames - 1, $"crouching {crouched} of 60 frames");
    }

    [Fact]
    public void AJumpingDummyJumpsAgainAndAgain()
    {
        var takeoffs = 0;
        var wasAirborne = false;
        Contacts(Set(DummyAction.Jump), _ => Buttons.None, 200, s =>
        {
            takeoffs += s.Fighters[1].Airborne && !wasAirborne ? 1 : 0;
            wasAirborne = s.Fighters[1].Airborne;
        });
        Assert.True(takeoffs >= 3, $"{takeoffs} jumps");
    }

    // A mid guarded standing and a low guarded crouching; nothing gets through.
    [Fact]
    public void BlockAllGuardsMidsStandingAndLowsCrouching()
    {
        var contacts = Contacts(Set(DummyAction.BlockAll), Attacking(Buttons.MediumPunch, Buttons.Down | Buttons.MediumKick), 200);
        Assert.Equal(2, contacts.Count);
        Assert.All(contacts, c => Assert.True(c.Blocked));
        Assert.False(contacts[0].Crouching);
        Assert.True(contacts[1].Crouching);
    }

    // Guarding only when something's coming: with nothing thrown at it, a blocking dummy holds no
    // direction and stays where it is, rather than walking back.
    [Theory]
    [InlineData(DummyAction.BlockAll)]
    [InlineData(DummyAction.BlockAfterFirstHit)]
    public void AGuardingDummyStandsItsGroundUntilAttacked(DummyAction action)
    {
        var dummy = Set(action);
        var start = GameState.NewMatch(1, Stage.Training, MatchMode.Training).Fighters[1].Position;
        var held = Buttons.None;
        var last = start;
        Contacts(dummy, s => s.Frame < 40 ? Buttons.Right : Buttons.None, 80, s =>
        {
            held |= s.Inputs[1].At(s.Frame - 1, s.Frame - 1);
            last = s.Fighters[1].Position;
        });
        Assert.Equal(Buttons.None, held);
        Assert.True(last.X >= start.X, "pushed along, never walking back");
    }

    // The first hit lands, the next is guarded; once left alone for a while, the next first hit lands.
    [Fact]
    public void BlockAfterFirstHitLetsOneThroughThenGuards()
    {
        var p1 = Attacking(Buttons.MediumPunch, Buttons.MediumPunch);
        var later = Attacking(Buttons.MediumPunch);
        Func<GameState, Buttons> script = s => s.Frame < 300 ? p1(s) : later(s);

        var contacts = Contacts(Set(DummyAction.BlockAfterFirstHit), script, 400);
        Assert.Equal(new[] { false, true, false }, contacts.ConvertAll(c => c.Blocked));
        Assert.True(contacts[2].Frame - contacts[1].Frame > Dummy.GuardHoldFrames);
    }

    // The dummy's damage stays up through the combo, then refills (training mode).
    [Fact]
    public void TheDummysHealthRefills()
    {
        var lowest = Data.Stats.Health;
        var final = 0;
        Contacts(Set(DummyAction.Stand), Attacking(Buttons.HeavyPunch), 120, s =>
        {
            lowest = Math.Min(lowest, s.Fighters[1].Health);
            final = s.Fighters[1].Health;
        });
        Assert.True(lowest < Data.Stats.Health);
        Assert.Equal(Data.Stats.Health, final);
    }

    // P2's buttons, made by a player controlling P2 (here a script), recorded; then from the same
    // start the dummy plays them back, and P2 does exactly the same, frame for frame.
    [Fact]
    public void PlaybackReproducesTheRecordingExactly()
    {
        Buttons Played(int f) => f switch
        {
            < 20 => Buttons.Left,
            20 => Buttons.LightPunch,
            < 40 => Buttons.None,
            < 45 => Buttons.Down | Buttons.Left,
            45 => Buttons.Down | Buttons.MediumKick,
            < 70 => Buttons.Down,
            _ => Buttons.Right,
        };

        var recording = new InputRecording();
        var state = GameState.NewMatch(1, Stage.Training, MatchMode.Training);
        var original = new List<GameState>();
        for (var f = 0; f < 90; f++)
        {
            recording.Add(Played(f), state.Fighters[1].Facing);
            Simulation.Step(ref state, new FrameInput(Buttons.None, Played(f)));
            original.Add(state);
        }

        var dummy = new Dummy { Action = DummyAction.Playback, Recording = recording, Loop = false };
        state = GameState.NewMatch(1, Stage.Training, MatchMode.Training);
        for (var f = 0; f < 90; f++)
        {
            Simulation.Step(ref state, new FrameInput(Buttons.None, dummy.Next(state, 1)));
            Assert.True(state.StateEquals(original[f]), $"frame {f} differs");
        }
        Assert.Equal(Buttons.None, dummy.Next(state, 1)); // played once, then nothing
    }

    [Fact]
    public void ALoopingPlaybackStartsOverAndAFullRecordingStops()
    {
        var recording = new InputRecording();
        recording.Add(Buttons.LightPunch, 1);
        recording.Add(Buttons.None, 1);
        var dummy = new Dummy { Action = DummyAction.Playback, Recording = recording, Loop = true };
        var state = GameState.NewMatch(1);
        var presses = new List<Buttons>();
        for (var i = 0; i < 5; i++)
            presses.Add(dummy.Next(state, 0));
        Assert.Equal(new[] { Buttons.LightPunch, Buttons.None, Buttons.LightPunch, Buttons.None, Buttons.LightPunch }, presses);

        var full = new InputRecording();
        for (var i = 0; i < InputRecording.MaxFrames + 30; i++)
            full.Add(Buttons.Right, 1);
        Assert.Equal(InputRecording.MaxFrames, full.Frames);
        Assert.True(full.Full);
    }

    // Recorded walking forward on the right (facing left), it walks forward from the left too.
    [Fact]
    public void ARecordingIsKeptRelativeToFacing()
    {
        var recording = new InputRecording();
        recording.Add(Buttons.Left | Buttons.Down | Buttons.HeavyKick, -1);
        Assert.Equal(Buttons.Left | Buttons.Down | Buttons.HeavyKick, recording.At(0, -1));
        Assert.Equal(Buttons.Right | Buttons.Down | Buttons.HeavyKick, recording.At(0, 1));
    }

    [Theory]
    [InlineData(ResetPosition.LeftCorner)]
    [InlineData(ResetPosition.RightCorner)]
    public void ACornerResetPutsTheDummyOnTheWallWithP1InFront(ResetPosition position)
    {
        var state = TrainingReset.Fresh(5, Stage.Training, MatchMode.Training, position);
        var left = position == ResetPosition.LeftCorner;
        var dummy = state.Fighters[1];
        var player = state.Fighters[0];

        Assert.Equal(left ? Pushing.MinX(state.Stage) : Pushing.MaxX(state.Stage), dummy.Position.X);
        Assert.Equal(TrainingReset.Gap, Fix.Abs(player.Position.X - dummy.Position.X));
        Assert.Equal(left ? 1 : -1, dummy.Facing);
        Assert.Equal(-dummy.Facing, player.Facing);
        Assert.Equal(Data.Stats.Health, dummy.Health);
        Assert.Equal(0, state.Frame);
        Assert.Equal(MatchMode.Training, state.Match.Mode);

        // Nothing moves or turns on its own.
        for (var i = 0; i < 30; i++)
            Simulation.Step(ref state, default);
        Assert.Equal(dummy.Position, state.Fighters[1].Position);
        Assert.Equal(player.Position, state.Fighters[0].Position);
        Assert.Equal(dummy.Facing, state.Fighters[1].Facing);
    }

    // The dummy replays show what their names say (a replay alone only pins behavior): who got
    // hit or guarded, how, and what the dummy did, from the recorded inputs.
    [Theory]
    [InlineData("dummy-stand", "hit hit")]
    [InlineData("dummy-crouch", "hit")]
    [InlineData("dummy-jump", "")]
    [InlineData("dummy-block-all", "blocked blocked-crouching")]
    [InlineData("dummy-block-after-first-hit", "hit blocked hit")]
    [InlineData("dummy-playback", "")]
    public void DummyReplaysShowWhatTheySay(string name, string contacts)
    {
        var replay = Replays.Replay.Read(System.IO.File.ReadAllText(System.IO.Path.Combine(RepoRoot(), "replays", name + ".sfr")));
        Assert.Equal(MatchMode.Training, replay.Mode);
        var state = GameState.NewMatch(replay.Seed, replay.Stage, replay.Mode);
        var seen = new List<string>();
        int crouching = 0, takeoffs = 0, lowestHealth = Data.Stats.Health;
        var wasAirborne = false;
        foreach (var input in replay.Inputs)
        {
            var before = state.Fighters[1];
            Simulation.Step(ref state, input);
            var p2 = state.Fighters[1];
            if (p2.Hitstun > before.Hitstun)
                seen.Add("hit");
            if (p2.Blockstun > before.Blockstun)
                seen.Add(p2.Action == (int)FighterAction.BlockCrouch ? "blocked-crouching" : "blocked");
            crouching += p2.Action is (int)FighterAction.Crouch or (int)FighterAction.CrouchDown or (int)FighterAction.Hitstun ? 1 : 0;
            takeoffs += p2.Airborne && !wasAirborne ? 1 : 0;
            wasAirborne = p2.Airborne;
            lowestHealth = Math.Min(lowestHealth, p2.Health);
        }

        Assert.Equal(contacts, string.Join(" ", seen));
        Assert.Equal(Data.Stats.Health, state.Fighters[1].Health); // training: refilled by the end
        switch (name)
        {
            case "dummy-stand":
                Assert.True(lowestHealth < Data.Stats.Health, "the hits did damage before the refill");
                break;
            case "dummy-crouch":
                // Down held throughout: crouching or going down on every frame, but for the hit.
                Assert.All(replay.Inputs, i => Assert.Equal(Buttons.Down, i.Player2));
                Assert.Equal(replay.Frames, crouching);
                break;
            case "dummy-jump":
                Assert.True(takeoffs >= 3, $"{takeoffs} jumps");
                break;
            case "dummy-playback":
                // The 40-frame recording on a loop: P2's buttons repeat every 40 frames.
                for (var f = 40; f < replay.Frames; f++)
                    Assert.Equal(replay.Inputs[f - 40].Player2, replay.Inputs[f].Player2);
                Assert.Contains(replay.Inputs, i => i.Player2 == Buttons.LightPunch);
                break;
        }
    }

    private static string RepoRoot()
    {
        var dir = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "StickFighter.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new System.IO.DirectoryNotFoundException("repo root");
    }

    [Fact]
    public void AMiddleResetIsTheMatchStart()
    {
        var reset = TrainingReset.Fresh(5, Stage.Training, MatchMode.Training, ResetPosition.Middle);
        Assert.True(reset.StateEquals(GameState.NewMatch(5, Stage.Training, MatchMode.Training)));
    }
}
