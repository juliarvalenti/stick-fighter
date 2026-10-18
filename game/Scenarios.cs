using System;
using System.Collections.Generic;
using StickFighter.Sim;

namespace StickFighter;

// A scripted, deterministic run of the game for CI: a seed, inputs by frame, which debug views
// are on, and which frames to capture. Inputs come from the script, so frame N is always the same
// state, and its picture is a pure function of it.
//
// Every visible feature adds a scenario; its golden images are the evidence it looks right.
public sealed record Scenario(
    string Name,
    ulong Seed,
    Func<int, FrameInput> Script,
    int[] Captures,
    int Frames,
    bool Boxes = false,
    bool Overlay = false,
    bool PacingCheck = false,
    Func<GameState, GameState>? Setup = null);

public static class Scenarios
{
    private static readonly FrameInput WalkIn = new(Buttons.Right, Buttons.Left);

    public static readonly IReadOnlyList<Scenario> All = new[]
    {
        // Both fighters walk together and stop at the pushboxes; boxes on.
        new Scenario("walk-in", 1, _ => WalkIn, Captures: new[] { 1, 30, 90 }, Frames: 90, Boxes: true),

        // Fighters start swapped, as after a jump-over, and must turn to face each other.
        new Scenario("cross-over", 1, _ => default, Captures: new[] { 2 }, Frames: 2, Boxes: true,
            Setup: s =>
            {
                (s.Fighters[0].Position, s.Fighters[1].Position) = (s.Fighters[1].Position, s.Fighters[0].Position);
                return s;
            }),

        // The frame-time overlay, with fixed numbers so the image is stable.
        new Scenario("overlay", 1, _ => WalkIn, Captures: new[] { 60 }, Frames: 60, Overlay: true),

        // Ten seconds of jittered 60 Hz frames through the real _Process path: every frame must run
        // exactly one tick, or walking would stutter.
        new Scenario("pacing", 1, f => f % 120 < 60 ? WalkIn : default, Captures: Array.Empty<int>(), Frames: 600,
            PacingCheck: true),
    };

    public static Scenario? Find(string name)
    {
        foreach (var scenario in All)
        {
            if (scenario.Name == name)
                return scenario;
        }
        return null;
    }

    public sealed class Source : IInputSource
    {
        private readonly Scenario _scenario;

        public Source(Scenario scenario) => _scenario = scenario;

        public FrameInput Read(int frame) => _scenario.Script(frame);
    }
}
