using System;
using System.Collections.Generic;
using StickFighter.Sim;
using StickFighter.Sim.Numerics;

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
    Func<GameState, GameState>? Setup = null,
    string? PoseSheet = null);

public static class Scenarios
{
    private static readonly FrameInput WalkIn = new(Buttons.Right, Buttons.Left);

    // Built on first use, after the static match scenarios below exist.
    private static IReadOnlyList<Scenario>? _all;

    public static IReadOnlyList<Scenario> All => _all ??= Build();

    private static IReadOnlyList<Scenario> Build()
    {
        var all = new List<Scenario>(Match);

        // One pose sheet per move: `sheet-5LP`, `sheet-j.HK`, ...
        foreach (var move in Sim.Characters.Characters.Placeholder.Moves)
            all.Add(new Scenario($"sheet-{move.Input}", 1, _ => default, Captures: new[] { 1 }, Frames: 1, PoseSheet: move.Input));
        return all;
    }

    private static readonly Scenario[] Match =
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

        // The data-driven poses at minimum distance (pushboxes touching), boxes on: standing, then
        // crouching, then both walking forward into each other. The arms must not cross.
        new Scenario("poses", 1, f => f < 20 ? default : f < 40 ? new FrameInput(Buttons.Down, Buttons.Down) : WalkIn,
            Captures: new[] { 10, 30, 52 }, Frames: 52, Boxes: true,
            Setup: s =>
            {
                s.Fighters[0].Position = new FixVec2(Fix.FromInt(610), s.Fighters[0].Position.Y);
                s.Fighters[1].Position = new FixVec2(Fix.FromInt(670), s.Fighters[1].Position.Y);
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
