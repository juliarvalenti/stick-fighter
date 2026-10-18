using System;
using StickFighter.Sim.Characters;
using StickFighter.Sim.Combat;
using StickFighter.Sim.Movement;
using StickFighter.Sim.Numerics;
using Xunit;

namespace StickFighter.Sim.Tests;

public class JumpTests
{
    private static Character Data => Characters.Characters.Placeholder;
    private static Stats Stats => Data.Stats;
    private static Fix Floor => Stage.Training.Floor;

    // Fighters at the given feet positions, standing and facing each other.
    private static GameState At(int p1X, int p2X)
    {
        var state = GameState.NewMatch(1);
        state.Fighters[0].Position = new FixVec2(Fix.FromInt(p1X), Floor);
        state.Fighters[1].Position = new FixVec2(Fix.FromInt(p2X), Floor);
        state.Fighters[0].Facing = p1X < p2X ? 1 : -1;
        state.Fighters[1].Facing = -state.Fighters[0].Facing;
        return state;
    }

    private static FighterAction ActionOf(in GameState state, int player) => (FighterAction)state.Fighters[player].Action;

    private static void Run(ref GameState state, int frames, FrameInput input)
    {
        for (var i = 0; i < frames; i++)
            Simulation.Step(ref state, input);
    }

    // Steps until P1 has landed (or a limit), returning how many frames it was airborne.
    private static int RunUntilLanded(ref GameState state, Func<int, FrameInput> input)
    {
        var airborne = 0;
        for (var i = 0; i < 200; i++)
        {
            Simulation.Step(ref state, input(i));
            if (state.Fighters[0].Airborne)
                airborne++;
            else if (airborne > 0)
                return airborne;
        }
        return airborne;
    }

    [Fact]
    public void TheArcLandsInExactlyTheAirborneFramesWithItsApexAtStandingHeight()
    {
        Assert.Equal(Stats.AirborneFrames, Jumping.ArcFrames(Stats.JumpVelocity, Stats.Gravity));

        var state = At(600, 1200);
        Run(ref state, Stats.PreJumpFrames + 1, new FrameInput(Buttons.Up, default));
        var highest = Fix.Zero;
        RunUntilLanded(ref state, _ =>
        {
            highest = Fix.Max(highest, Floor - state.Fighters[0].Position.Y);
            return default;
        });
        Assert.InRange(highest.RoundToInt(), 285, 300); // SF4's 1.76 units, matched to our height
    }

    [Theory]
    [InlineData(Buttons.Up, 0)]                 // 8
    [InlineData(Buttons.Up | Buttons.Right, 1)] // 9, forward for P1
    [InlineData(Buttons.Up | Buttons.Left, -1)] // 7, back for P1
    public void EachJumpHasPreJumpAFixedArcAndLanding(Buttons up, int direction)
    {
        var state = At(1000, 1600);
        var x = state.Fighters[0].Position.X;

        for (var i = 0; i < Stats.PreJumpFrames; i++)
        {
            Simulation.Step(ref state, new FrameInput(up, default));
            Assert.Equal(FighterAction.PreJump, ActionOf(state, 0));
            Assert.False(state.Fighters[0].Airborne);
            Assert.Equal(x, state.Fighters[0].Position.X);
        }

        // Takeoff on the next frame, in the direction held then; after that no input changes the arc.
        Simulation.Step(ref state, new FrameInput(up, default));
        Assert.True(state.Fighters[0].Airborne);
        // Takeoff, the frames in the air, and touchdown: the arc's frames.
        var arc = 1 + RunUntilLanded(ref state, i => new FrameInput(i % 2 == 0 ? Buttons.Left : Buttons.Right, default)) + 1;
        Assert.Equal(Stats.AirborneFrames, arc);
        Assert.Equal(FighterAction.Landing, ActionOf(state, 0));
        Assert.Equal(Floor, state.Fighters[0].Position.Y);

        var drift = direction > 0 ? Stats.JumpForward : direction < 0 ? -Stats.JumpBack : Fix.Zero;
        Assert.Equal(x + drift * Stats.AirborneFrames, state.Fighters[0].Position.X);
    }

    [Fact]
    public void JumpingOverTheOpponentSwitchesSidesAndTurnsOnLanding()
    {
        var state = At(900, 1000);
        Run(ref state, Stats.PreJumpFrames + 1, new FrameInput(Buttons.Up | Buttons.Right, default));

        var p2TurnedInTheAir = false;
        while (state.Fighters[0].Airborne)
        {
            Simulation.Step(ref state, default);
            if (state.Fighters[0].Airborne)
            {
                Assert.Equal(1, state.Fighters[0].Facing); // no turning in the air
                p2TurnedInTheAir |= state.Fighters[1].Facing == 1;
            }
        }

        Assert.True(state.Fighters[0].Position.X > state.Fighters[1].Position.X, "P1 landed on the other side");
        Assert.Equal(-1, state.Fighters[0].Facing); // turned on the landing frame
        Assert.Equal(1, state.Fighters[1].Facing);
        Assert.True(p2TurnedInTheAir, "P2, on the ground and free, turns as soon as P1 crosses");
        Assert.True(state.Fighters[0].Position.X - state.Fighters[1].Position.X >= Collision.Body.PushHalfWidth * 2, "landed clear, not inside");
    }

    [Fact]
    public void OneAirAttackPerJump()
    {
        var state = At(1000, 1600);
        Run(ref state, Stats.PreJumpFrames + 1, new FrameInput(Buttons.Up, default));
        Simulation.Step(ref state, new FrameInput(Buttons.LightKick, default));
        Assert.Equal("j.LK", Data.Moves[state.Fighters[0].Move].Input);
        var lk = Data.Moves[state.Fighters[0].Move];
        Run(ref state, lk.TotalFrames, default);
        Assert.Equal(FighterAction.Jump, ActionOf(state, 0));

        Simulation.Step(ref state, new FrameInput(Buttons.HeavyPunch, default));
        Assert.Equal(FighterAction.Jump, ActionOf(state, 0)); // used up
    }

    [Fact]
    public void AnEmptyLandingIsFree()
    {
        var state = At(1000, 1600);
        Run(ref state, Stats.PreJumpFrames + 1, new FrameInput(Buttons.Up, default));
        RunUntilLanded(ref state, _ => default);
        Assert.Equal(FighterAction.Landing, ActionOf(state, 0));

        Simulation.Step(ref state, new FrameInput(Buttons.Right, default));
        Assert.Equal(FighterAction.WalkForward, ActionOf(state, 0));
    }

    [Fact]
    public void AnEmptyLandingPlaysOutIfNothingIsPressed()
    {
        var state = At(1000, 1600);
        Run(ref state, Stats.PreJumpFrames + 1, new FrameInput(Buttons.Up, default));
        RunUntilLanded(ref state, _ => default);
        for (var i = 1; i < Stats.LandingFrames; i++)
        {
            Simulation.Step(ref state, default);
            Assert.Equal(FighterAction.Landing, ActionOf(state, 0));
        }
        Simulation.Step(ref state, default);
        Assert.Equal(FighterAction.Stand, ActionOf(state, 0));
    }

    [Fact]
    public void LandingAfterAnAirAttackHasRecoveryThenGuardOnly()
    {
        var state = At(1000, 1600);
        Run(ref state, Stats.PreJumpFrames + 1, new FrameInput(Buttons.Up, default));
        Run(ref state, 20, default);
        Simulation.Step(ref state, new FrameInput(Buttons.HeavyKick, default)); // late: still out on landing
        RunUntilLanded(ref state, _ => default);
        Assert.Equal(FighterAction.Landing, ActionOf(state, 0));
        Assert.False(Blocking.CanGuard(state.Fighters[0]));

        // The next frame is still true recovery: walking does nothing, and neither does guarding.
        Simulation.Step(ref state, new FrameInput(Buttons.Left, default));
        Assert.Equal(FighterAction.Landing, ActionOf(state, 0));
        Assert.False(Blocking.CanGuard(state.Fighters[0]));

        // Then guarding only: back guards, forward doesn't walk.
        Simulation.Step(ref state, new FrameInput(Buttons.Right, default));
        Assert.Equal(FighterAction.Landing, ActionOf(state, 0));
        Assert.True(Blocking.CanGuard(state.Fighters[0]));
        Simulation.Step(ref state, new FrameInput(Buttons.Left, default));
        Assert.Equal(FighterAction.BlockStand, ActionOf(state, 0));
    }

    [Fact]
    public void NoGuardingInTheAirOrInPreJump()
    {
        var state = At(1000, 1600);
        Simulation.Step(ref state, new FrameInput(Buttons.Up | Buttons.Left, default));
        Assert.Equal(FighterAction.PreJump, ActionOf(state, 0));
        Assert.False(Blocking.CanGuard(state.Fighters[0]));

        Run(ref state, Stats.PreJumpFrames, new FrameInput(Buttons.Up | Buttons.Left, default));
        Assert.True(state.Fighters[0].Airborne);
        Assert.False(Blocking.CanGuard(state.Fighters[0]));
    }

    // The owed Blocking cases, now that there are jumps.

    // P1 jumps in at a standing P2 from `p1X` and presses `button` on the first frame (searching
    // forward) where the attack connects with P2 holding `p2` from the press on (before it, P2
    // stands still rather than walking off). Returns the state just after contact.
    private static GameState JumpIn(int p1X, int p2X, Buttons jump, Buttons button, Buttons p2) =>
        TryJumpIn(p1X, p2X, jump, button, p2, fromBehind: false) ?? throw new InvalidOperationException("the jump-in never connected");

    // As JumpIn, but null if it never connects; with `fromBehind`, only a hit landing with P1 past
    // P2 (a crossup) counts.
    private static GameState? TryJumpIn(int p1X, int p2X, Buttons jump, Buttons button, Buttons p2, bool fromBehind)
    {
        for (var press = 4; press < Stats.AirborneFrames; press++)
        {
            var state = At(p1X, p2X);
            Run(ref state, Stats.PreJumpFrames + 1, new FrameInput(jump, default));
            Run(ref state, press, default);
            Simulation.Step(ref state, new FrameInput(button, p2));
            for (var i = 0; i < 40 && state.Fighters[0].Airborne && !state.Fighters[0].MoveHit; i++)
                Simulation.Step(ref state, new FrameInput(Buttons.None, p2));
            if (state.Fighters[0].MoveHit && (!fromBehind || state.Fighters[0].Position.X > state.Fighters[1].Position.X))
                return state;
        }
        return null;
    }

    // A crossup: P1 jumps over P2 and the jumping normal lands just after P1 has passed P2. Searches
    // the start distance and air button for one that does (our air normals strike forward, so a
    // crossup clips the defender just after the attacker's feet pass it).
    private static GameState Crossup(Buttons p2)
    {
        foreach (var button in new[] { Buttons.MediumKick, Buttons.LightKick, Buttons.HeavyKick, Buttons.HeavyPunch })
        {
            for (var gap = 60; gap <= 200; gap += 10)
            {
                if (TryJumpIn(1000 - gap, 1000, Buttons.Up | Buttons.Right, button, p2, fromBehind: true) is { } state)
                    return state;
            }
        }
        throw new InvalidOperationException("no crossup found");
    }

    [Fact]
    public void AJumpInIsBlockedStanding()
    {
        // P2 is on the right, so back is Right.
        var blocked = JumpIn(800, 1000, Buttons.Up | Buttons.Right, Buttons.HeavyKick, Buttons.Right);
        Assert.True(blocked.Fighters[1].Blockstun > 0, "a standing guard blocks the jump-in");
        Assert.Equal(0, blocked.Fighters[1].Hitstun);
    }

    [Fact]
    public void AJumpInHitsACrouchBlocker()
    {
        var hit = JumpIn(800, 1000, Buttons.Up | Buttons.Right, Buttons.HeavyKick, Buttons.Right | Buttons.Down);
        Assert.True(hit.Fighters[1].Hitstun > 0, "jump-ins are high: a crouching guard is hit");
    }

    [Fact]
    public void NeitherAirborneNorPreJumpFightersCanBeThrown()
    {
        Assert.True(Throws.Throwable(new FighterState { Grab = -1 }));
        Assert.False(Throws.Throwable(new FighterState { Grab = -1, Airborne = true, Action = (int)FighterAction.Jump }));
        Assert.False(Throws.Throwable(new FighterState { Grab = -1, Action = (int)FighterAction.PreJump }));
    }

    [Fact]
    public void LandingFramesCanBeTeched()
    {
        Assert.True(Throws.Techable(new FighterState { Action = (int)FighterAction.Landing }));
    }

    // The jump-related throw replays show what their names say: a throw active during pre-jump or
    // the air never grabs, and a throw in the landing frames grabs and is teched.
    [Theory]
    [InlineData("throw-prejump")]
    [InlineData("throw-airborne")]
    [InlineData("throw-landing-tech")]
    public void ThrowReplaysAroundJumpsShowWhatTheySay(string name)
    {
        var replay = Replays.Replay.Read(System.IO.File.ReadAllText(System.IO.Path.Combine(RepoRoot(), "replays", name + ".sfr")));
        var state = GameState.NewMatch(replay.Seed, replay.Stage);
        bool throwActive = false, grabbed = false, grabbedLanding = false, jumped = false, knockedDown = false;
        foreach (var input in replay.Inputs)
        {
            var before = state.Fighters[1];
            Simulation.Step(ref state, input);
            // Active and whiffing, or already holding P2 (a grab turns the throw into the hold at once).
            if ((Collision.Body.MoveOf(state.Fighters[0]) is { Kind: MoveKind.Throw } t && t.IsActive(state.Fighters[0].ActionFrame))
                || state.Fighters[0].Grab >= 0)
                throwActive = true;
            if (state.Fighters[1].Grab >= 0 && before.Grab < 0)
            {
                grabbed = true;
                grabbedLanding |= before.Action == (int)FighterAction.Landing;
            }
            jumped |= state.Fighters[1].Airborne;
            knockedDown |= state.Fighters[1].KnockedDown;
        }

        Assert.True(throwActive, "P1's throw came out");
        switch (name)
        {
            case "throw-prejump":
            case "throw-airborne":
                Assert.False(grabbed, "the throw grabbed P2");
                Assert.True(jumped, "P2 jumped");
                break;
            case "throw-landing-tech":
                Assert.True(grabbedLanding, "P2 was grabbed in its landing frames");
                Assert.False(knockedDown, "P2 teched rather than being thrown");
                break;
        }
    }

    // A replay only pins checksums; this checks each jump replay still shows what its name says.
    [Theory]
    [InlineData("jump-in-blocked")]
    [InlineData("jump-in-crouch-hit")]
    [InlineData("air-hit")]
    [InlineData("crossup-blocked")]
    [InlineData("crossup-hit")]
    [InlineData("jump-over")]
    public void JumpReplaysShowWhatTheySay(string name)
    {
        var replay = Replays.Replay.Read(System.IO.File.ReadAllText(System.IO.Path.Combine(RepoRoot(), "replays", name + ".sfr")));
        var state = GameState.NewMatch(replay.Seed, replay.Stage);
        bool blocked = false, hit = false, hitFromBehind = false, blockedFromBehind = false, hitInAir = false;
        foreach (var input in replay.Inputs)
        {
            var before = state.Fighters[1];
            Simulation.Step(ref state, input);
            var behind = state.Fighters[0].Position.X > state.Fighters[1].Position.X;
            if (state.Fighters[1].Blockstun > before.Blockstun)
            {
                blocked = true;
                blockedFromBehind |= behind;
            }
            if (state.Fighters[1].Hitstun > before.Hitstun)
            {
                hit = true;
                hitFromBehind |= behind;
                hitInAir |= state.Fighters[1].Airborne;
            }
        }

        switch (name)
        {
            case "jump-in-blocked": Assert.True(blocked && !hit); break;
            case "jump-in-crouch-hit": Assert.True(hit && !blocked); break;
            case "air-hit": Assert.True(hitInAir); break;
            case "crossup-blocked": Assert.True(blockedFromBehind && !hit); break;
            case "crossup-hit": Assert.True(hitFromBehind && !blocked); break;
            case "jump-over": Assert.True(state.Fighters[0].Position.X > state.Fighters[1].Position.X && state.Fighters[0].Facing == -1); break;
        }
    }

    private static string RepoRoot()
    {
        var dir = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "StickFighter.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new System.IO.DirectoryNotFoundException("repo root");
    }

    // j.LK is the crossup: on some active frame its hitbox reaches behind the attacker's centre line,
    // which is what lets it hit a defender it has just passed.
    [Fact]
    public void JumpingLightKickReachesBehindTheAttacker()
    {
        var lk = Data.FindMove("j.LK")!;
        var reachesBehind = false;
        for (var frame = lk.FirstActiveFrame; frame < lk.FirstActiveFrame + lk.Active; frame++)
        {
            var fighter = new FighterState
            {
                Position = new FixVec2(Fix.FromInt(1000), Fix.FromInt(300)),
                Facing = 1,
                Action = (int)FighterAction.Attack,
                ActionFrame = frame,
                Move = Data.Moves.IndexOf(lk),
                Airborne = true,
            };
            var hit = Collision.Body.Shapes(fighter).Hit[0];
            var behind = Fix.Min(hit.A.X, hit.B.X) - hit.Radius;
            reachesBehind |= behind < fighter.Position.X;
        }
        Assert.True(reachesBehind, "j.LK's hitbox never reaches behind the attacker");
    }

    [Fact]
    public void ACrossupIsBlockedByHoldingAwayFromWhereTheAttackerIs()
    {
        // P1 jumps over P2 from close and hits on the far side, behind P2's original facing. Away
        // from the attacker is then Left for P2, even though P2 faced left (Left was forward).
        var blocked = Crossup(Buttons.Left);
        Assert.True(blocked.Fighters[1].Blockstun > 0, "holding away by position blocks the crossup");

        // Holding back by the old facing (Right) is holding toward the attacker: hit.
        var hit = Crossup(Buttons.Right);
        Assert.True(hit.Fighters[1].Hitstun > 0, "holding back by facing is hit by the crossup");
    }

    [Fact]
    public void AFighterHitInTheAirCantGuardFallsAndLandsWithRecovery()
    {
        // P2 jumps straight up in front of P1, holding back (no air blocking), and P1 anti-airs with
        // 5HP on the first frame that connects.
        for (var press = 0; press < Stats.AirborneFrames; press++)
        {
            var state = At(900, 1000);
            Run(ref state, Stats.PreJumpFrames + 1, new FrameInput(default, Buttons.Up));
            Run(ref state, press, new FrameInput(default, Buttons.Right));
            Simulation.Step(ref state, new FrameInput(Buttons.HeavyPunch, Buttons.Right));
            for (var i = 0; i < 20 && !state.Fighters[0].MoveHit; i++)
                Simulation.Step(ref state, new FrameInput(default, Buttons.Right));
            if (!state.Fighters[0].MoveHit || !state.Fighters[1].Airborne)
                continue;

            Assert.True(state.Fighters[1].Hitstun > 0 || state.Fighters[1].Action == (int)FighterAction.Hitstun);
            Assert.Equal(0, state.Fighters[1].Blockstun);
            for (var i = 0; i < 100 && state.Fighters[1].Airborne; i++)
                Simulation.Step(ref state, new FrameInput(default, Buttons.Right));
            Assert.Equal(FighterAction.Landing, ActionOf(state, 1));
            Assert.True(state.Fighters[1].AirAttackUsed, "a landing after an air hit has recovery");
            return;
        }
        throw new InvalidOperationException("the anti-air never hit P2 in the air");
    }
}
