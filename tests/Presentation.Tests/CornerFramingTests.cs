using System;
using StickFighter.Sim;
using StickFighter.Sim.Training;
using Xunit;

namespace StickFighter.Presentation.Tests;

// The camera frames what's drawn of the fighters, not their pushboxes: a cornered fighter's arms
// and crouch-block lean reach past the wall, and at the separation limit the two are drawn wider
// than the screen. Each test plays a real match frame by frame through CameraMath.Frame (what
// StageCamera does) and checks every frame that both fighters, as drawn, are fully on screen.
public class CornerFramingTests
{
    private const float ViewWidth = Stage.ScreenWidth;

    private static float X(Sim.Numerics.Fix value) => value.Raw / (float)Sim.Numerics.Fix.OneRaw;

    // Plays `frames` frames of P1's buttons against the dummy (guarding everything), one tick a
    // display frame, and returns how far past each wall anything was drawn, and the smallest zoom.
    private static (float PastLeft, float PastRight, float MinZoom) Play(GameState state, Func<GameState, Buttons> p1, Func<GameState, Buttons> p2, int frames)
    {
        float? camera = null;
        float pastLeft = 0, pastRight = 0, minZoom = 1;
        for (var f = 0; f < frames; f++)
        {
            Simulation.Step(ref state, new FrameInput(p1(state), p2(state)));
            var stage = state.Stage;
            var (drawnLeft, drawnRight) = DrawnBounds.Of(state);
            var (x, zoom) = CameraMath.Frame(camera, 1, X(state.Fighters[0].Position.X), X(state.Fighters[1].Position.X),
                drawnLeft, drawnRight, X(stage.LeftWall), X(stage.RightWall), ViewWidth);
            camera = x;

            var half = ViewWidth / zoom / 2;
            Assert.True(drawnLeft >= x - half && drawnRight <= x + half,
                $"frame {state.Frame}: drawn {drawnLeft:0.#}..{drawnRight:0.#}, view {x - half:0.#}..{x + half:0.#}");
            pastLeft = Math.Max(pastLeft, X(stage.LeftWall) - drawnLeft);
            pastRight = Math.Max(pastRight, drawnRight - X(stage.RightWall));
            minZoom = Math.Min(minZoom, zoom);
        }
        return (pastLeft, pastRight, minZoom);
    }

    // P1 walks in on the cornered dummy and throws lows and mids; the dummy guards (crouching
    // against the lows, leaning back past the wall). Neither is ever cut by the screen edge.
    [Theory]
    [InlineData(ResetPosition.LeftCorner)]
    [InlineData(ResetPosition.RightCorner)]
    public void ACorneredFighterIsDrawnPastTheWallAndStaysOnScreen(ResetPosition corner)
    {
        var state = TrainingReset.Fresh(1, Stage.Training, MatchMode.Training, corner);
        var forward = corner == ResetPosition.LeftCorner ? Buttons.Left : Buttons.Right;
        var dummy = new Dummy { Action = DummyAction.BlockAll };
        Buttons P1(GameState s)
        {
            var close = Math.Abs(X(s.Fighters[1].Position.X) - X(s.Fighters[0].Position.X)) <= 70;
            var free = s.Fighters[0].Action is (int)Sim.Characters.FighterAction.Stand or (int)Sim.Characters.FighterAction.WalkForward;
            return !close ? forward : free ? (s.Frame % 2 == 0 ? Buttons.Down | Buttons.MediumKick : Buttons.MediumPunch) : Buttons.None;
        }

        var (pastLeft, pastRight, _) = Play(state, P1, s => dummy.Next(s, 1), 240);
        var past = corner == ResetPosition.LeftCorner ? pastLeft : pastRight;
        Assert.True(past > 0, "the cornered fighter was drawn past the wall (the case being tested)");
    }

    // Both walk back to the separation limit: pushboxes a screen apart, bodies drawn wider. The
    // camera zooms out a touch rather than cutting either off.
    [Fact]
    public void AtTheSeparationLimitTheCameraZoomsOutRatherThanCutAnyoneOff()
    {
        var state = GameState.NewMatch(1);
        var (_, _, minZoom) = Play(state, _ => Buttons.Left, _ => Buttons.Right, 150);
        Assert.True(minZoom < 1f, "zoomed out at the limit");
        Assert.True(minZoom > 0.85f, $"only a touch ({minZoom:0.###})");
    }

    // Mid-stage nothing changes: no zoom, and the camera on the fighters' midpoint.
    [Fact]
    public void MidStageTheFramingIsAsBefore()
    {
        var state = GameState.NewMatch(1);
        var (left, right) = DrawnBounds.Of(state);
        var p1 = X(state.Fighters[0].Position.X);
        var p2 = X(state.Fighters[1].Position.X);
        var (x, zoom) = CameraMath.Frame(null, 0, p1, p2, left, right, 0, 2 * Stage.ScreenWidth, ViewWidth);
        Assert.Equal(1f, zoom);
        Assert.Equal((p1 + p2) / 2, x);
    }

    [Fact]
    public void FrameZoomFitsTheDrawnWidthPlusPadding()
    {
        Assert.Equal(1f, CameraMath.FrameZoom(100, 100 + ViewWidth - 2 * CameraMath.FramePad, ViewWidth));
        Assert.Equal(ViewWidth / (ViewWidth + 100), CameraMath.FrameZoom(0, ViewWidth + 100 - 2 * CameraMath.FramePad, ViewWidth), 4);
    }
}
