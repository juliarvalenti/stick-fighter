using System.Linq;
using StickFighter.Sim;
using StickFighter.Training;
using Xunit;

namespace StickFighter.Presentation.Tests;

public class InputLogTests
{
    // The training-input-display scenario's P1 log, row by row: what its golden must show.
    private static readonly (string Notation, int Start, int Frames)[] Expected =
    {
        ("5", 0, 5),
        ("2", 5, 3), ("3", 8, 2), ("6", 10, 2), ("6LP", 12, 1), // 236P
        ("5", 13, 7),
        ("6", 20, 3), ("5", 23, 2), ("6", 25, 3), // 66
        ("5", 28, 7),
        ("5MP", 35, 1), ("5LP+MP", 36, 2), // the plink: LP joins a held MP
        ("5", 38, 7),
        ("4", 45, 30), // holding back
        ("5", 75, 5),
    };

    [Fact]
    public void TheDemoLogsRowByRowFromTheSimsOwnHistory()
    {
        var (p1, p2) = Run(stepsPerRead: 1);
        Assert.Equal(Expected, p1.Rows.Select(r => (InputLog.Notation(r), r.Start, r.Frames)));
        Assert.Equal(new[] { ("5", 0, InputDemo.Frames) }, p2.Rows.Select(r => (InputLog.Notation(r), r.Start, r.Frames)));
    }

    [Fact]
    public void SeveralTicksInOneDisplayFrameLogTheSame()
    {
        // The display reads once per display frame; at a low frame rate several ticks run between.
        var (p1, _) = Run(stepsPerRead: 4);
        Assert.Equal(Expected, p1.Rows.Select(r => (InputLog.Notation(r), r.Start, r.Frames)));
    }

    [Fact]
    public void ARollbackThatChangesRecentInputsCorrectsTheLog()
    {
        var log = new InputLog();
        for (var frame = 0; frame < 10; frame++)
            log.Observe(frame, frame < 6 ? Buttons.None : Buttons.Right, facing: 1);
        Assert.Equal(new[] { ("5", 0, 6), ("6", 6, 4) }, log.Rows.Select(r => (InputLog.Notation(r), r.Start, r.Frames)));

        // Frames 4 on are resimulated with down instead.
        for (var frame = 4; frame < 10; frame++)
            log.Observe(frame, Buttons.Down, facing: 1);
        Assert.Equal(new[] { ("5", 0, 4), ("2", 4, 6) }, log.Rows.Select(r => (InputLog.Notation(r), r.Start, r.Frames)));
    }

    [Fact]
    public void DirectionsAreRelativeToFacing()
    {
        var log = new InputLog();
        log.Observe(0, Buttons.Left, facing: -1); // facing left, left is forward
        log.Observe(1, Buttons.Left | Buttons.Down, facing: 1); // facing right, down-left is down-back
        Assert.Equal(new[] { "6", "1" }, log.Rows.Select(InputLog.Notation));
    }

    [Fact]
    public void SocdIsAlreadyCleanedInTheHistory()
    {
        // Left and right together reach the history as neutral, so the log shows 5.
        var state = GameState.NewMatch(1);
        Simulation.Step(ref state, new FrameInput(Buttons.Left | Buttons.Right, Buttons.None));
        var log = new InputLog();
        log.Read(state.Inputs[0], state.Frame - 1, state.Fighters[0].Facing);
        Assert.Equal("5", InputLog.Notation(log.Rows.Single()));
    }

    // Plays the demo through the simulation, reading the history the way the display does.
    private static (InputLog P1, InputLog P2) Run(int stepsPerRead)
    {
        var state = GameState.NewMatch(1);
        var p1 = new InputLog();
        var p2 = new InputLog();
        for (var frame = 0; frame < InputDemo.Frames; frame++)
        {
            Simulation.Step(ref state, InputDemo.Script(frame));
            if ((frame + 1) % stepsPerRead != 0 && frame != InputDemo.Frames - 1)
                continue;
            p1.Read(state.Inputs[0], state.Frame - 1, state.Fighters[0].Facing);
            p2.Read(state.Inputs[1], state.Frame - 1, state.Fighters[1].Facing);
        }
        return (p1, p2);
    }
}
