using StickFighter.Sim.Characters;
using StickFighter.Sim.Combat;
using StickFighter.Sim.Numerics;
using Xunit;

namespace StickFighter.Sim.Tests;

public class CancelsTests
{
    private static Character Data => Characters.Characters.Placeholder;

    private static int Move(string input) => Data.Moves.IndexOf(Data.FindMove(input)!);

    // P1 at 1000 facing right, P2 standing `gap` to its right: at 60 every ground normal reaches.
    private static GameState Apart(int gap = 60)
    {
        var state = GameState.NewMatch(1);
        state.Fighters[0].Position = new FixVec2(Fix.FromInt(1000), state.Fighters[0].Position.Y);
        state.Fighters[1].Position = new FixVec2(Fix.FromInt(1000 + gap), state.Fighters[1].Position.Y);
        return state;
    }

    private static void Step(ref GameState state, Buttons p1 = default, Buttons p2 = default) =>
        Simulation.Step(ref state, new FrameInput(p1, p2));

    // P1 presses `first` (holding `hold` with it), then steps on holding `hold` until it connects.
    private static void Connect(ref GameState state, Buttons first, Buttons hold = default, Buttons p2 = default)
    {
        Step(ref state, first | hold, p2);
        for (var i = 0; i < 20 && !state.Fighters[0].MoveHit; i++)
            Step(ref state, hold, p2);
        Assert.True(state.Fighters[0].MoveHit, "the first normal connected");
    }

    // Steps through the hitstop (P1 holding `hold`, pressing `press` on its first frozen frame).
    private static void ThroughHitstop(ref GameState state, Buttons press, Buttons hold = default)
    {
        Assert.True(Hitstop.Frozen(state, state.Frame));
        Step(ref state, press | hold);
        while (Hitstop.Frozen(state, state.Frame))
            Step(ref state, hold);
    }

    [Fact]
    public void AJabPressedInItsHitstopChainsIntoAJab()
    {
        var state = Apart();
        Connect(ref state, Buttons.LightPunch);
        var first = state.Fighters[0].LastMoveFrame;
        ThroughHitstop(ref state, Buttons.LightPunch);
        Step(ref state);
        Assert.Equal(Move("5LP"), state.Fighters[0].Move);
        Assert.Equal(0, state.Fighters[0].ActionFrame);
        Assert.NotEqual(first, state.Fighters[0].LastMoveFrame);
    }

    // Light punches chain into light punches only: a kick is a step up in strength.
    [Fact]
    public void AJabDoesntChainUpIntoAKick()
    {
        var state = Apart();
        Connect(ref state, Buttons.LightPunch);
        ThroughHitstop(ref state, Buttons.LightKick);
        var jab = Data.FindMove("5LP")!;
        while (state.Fighters[0].Move == Move("5LP") && state.Fighters[0].ActionFrame + 1 < jab.TotalFrames)
        {
            Step(ref state);
            Assert.NotEqual(Move("5LK"), state.Fighters[0].Move);
        }
    }

    // A light kick chains down into a light punch.
    [Fact]
    public void AShortChainsIntoAJab()
    {
        var state = Apart();
        Connect(ref state, Buttons.LightKick);
        ThroughHitstop(ref state, Buttons.LightPunch);
        Step(ref state);
        Assert.Equal(Move("5LP"), state.Fighters[0].Move);
        Assert.Equal(0, state.Fighters[0].ActionFrame);
    }

    // A medium has no chains: the jab waits for it to recover.
    [Fact]
    public void AMediumDoesntChain()
    {
        var state = Apart();
        Connect(ref state, Buttons.MediumPunch);
        ThroughHitstop(ref state, Buttons.LightPunch);
        Step(ref state);
        Assert.Equal(Move("5MP"), state.Fighters[0].Move);
    }

    // Pressed after the chain window (but before the jab recovers), the next jab doesn't chain: it
    // comes out from the buffer once the first has finished, a link.
    [Fact]
    public void AfterTheWindowAJabLinksInstead()
    {
        var state = Apart();
        Connect(ref state, Buttons.LightPunch);
        ThroughHitstop(ref state, Buttons.None);
        for (var i = 0; i < Data.Stats.ChainWindow; i++)
            Step(ref state);
        Assert.False(Cancels.Open(state.Fighters[0], Data.Stats.ChainWindow), "the window has closed");
        var jab = Data.FindMove("5LP")!;
        Assert.True(state.Fighters[0].ActionFrame + 1 < jab.TotalFrames, "the jab is still recovering");

        Step(ref state, Buttons.LightPunch);
        var before = state;
        while (state.Fighters[0].ActionFrame != 0)
        {
            before = state;
            Step(ref state);
        }
        Assert.Equal(jab.TotalFrames - 1, before.Fighters[0].ActionFrame);
    }

    // The motion's window skips hitstop too: 2HP's down is the 2, the 3 is held through the whole
    // freeze and the 6 + punch come on its last frame, more frames after the 2 than the window allows
    // counting the freeze, but within it counting only frames that ran. The fireball cancels the 2HP.
    [Fact]
    public void AMotionReachesBackAcrossTheHitstop()
    {
        var state = Apart();
        Connect(ref state, Buttons.Down | Buttons.HeavyPunch, Buttons.Down);
        var contact = state.Frame - 1;
        while (Hitstop.Frozen(state, state.Frame + 1))
            Step(ref state, Buttons.Down | Buttons.Right);
        Step(ref state, Buttons.Right | Buttons.LightPunch);
        Assert.True(state.Frame - 1 - contact >= Data.Stats.MotionWindow, "the 2 is out of the window counting frozen frames");
        Step(ref state);
        Assert.Equal(Specials.Fireball, state.Fighters[0].Move);
    }

    // 5MK isn't special-cancelable: the fireball motion done through it gives nothing until it ends.
    [Fact]
    public void ANormalThatIsntCancelableDoesntCancelIntoASpecial()
    {
        var state = Apart();
        Connect(ref state, Buttons.MediumKick);
        Step(ref state, Buttons.Down);
        Step(ref state, Buttons.Down | Buttons.Right);
        Step(ref state, Buttons.Right | Buttons.HeavyPunch);
        while (Hitstop.Frozen(state, state.Frame))
            Step(ref state);
        Step(ref state);
        Assert.Equal(Move("5MK"), state.Fighters[0].Move);
    }

    // Even a special marked as connected (projectiles never mark it, so this can't happen in play)
    // doesn't cancel into a normal: cancels only go forwards.
    [Fact]
    public void ASpecialNeverCancelsIntoANormal()
    {
        var state = Apart(400);
        state.Fighters[0].Action = (int)FighterAction.Attack;
        state.Fighters[0].Move = Specials.Fireball;
        state.Fighters[0].ActionFrame = 14;
        state.Fighters[0].MoveHit = true;
        state.Fighters[0].ContactFrame = 14;
        Step(ref state, Buttons.LightPunch);
        Assert.Equal(Specials.Fireball, state.Fighters[0].Move);
        Assert.Equal(15, state.Fighters[0].ActionFrame);
    }

    // P2 in blockstun guards whatever its stick does: a mid is blocked with the stick let go.
    [Fact]
    public void BlockstunKeepsAFighterGuarding()
    {
        var state = InBlockstun();
        Connect(ref state, Buttons.LightPunch);
        Assert.True(state.Fighters[1].Blockstun > 0);
        Assert.Equal(0, state.Fighters[1].Hitstun);
    }

    // Only the height follows the stick: standing in blockstun, a low still hits; crouching, it's blocked.
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void InBlockstunTheHeightFollowsDown(bool holdDown, bool hit)
    {
        var state = InBlockstun();
        Connect(ref state, Buttons.Down | Buttons.LightKick, Buttons.Down, holdDown ? Buttons.Down : Buttons.None);
        Assert.Equal(hit, state.Fighters[1].Hitstun > 0);
    }

    // P2 point-blank, in the middle of a long blockstun.
    private static GameState InBlockstun()
    {
        var state = Apart();
        state.Fighters[1].Blockstun = 30;
        state.Fighters[1].Action = (int)FighterAction.BlockStand;
        return state;
    }
}
