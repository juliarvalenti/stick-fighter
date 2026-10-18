using System;
using System.Collections.Generic;
using StickFighter.Sim;
using StickFighter.Sim.Numerics;
using StickFighter.StyleFrames;

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
    string? PoseSheet = null,
    Func<GameState, GameState>? EachFrame = null,
    Func<Godot.Control>? StyleFrame = null);

public static class Scenarios
{
    private static readonly FrameInput WalkIn = new(Buttons.Right, Buttons.Left);

    // Built on first use, after the static match scenarios below exist.
    private static IReadOnlyList<Scenario>? _all;

    public static IReadOnlyList<Scenario> All => _all ??= Build();

    private static IReadOnlyList<Scenario> Build()
    {
        var all = new List<Scenario>(Match)
        {
            Sequence("normals", GroundNormals),

            // Blocking: a mid blocked standing, a low blocked crouching, and a low that hits a
            // standing guard. P2 holds back (away from P1) throughout.
            Sequence("blocking", new[]
            {
                ("5MP", Buttons.MediumPunch, Buttons.Right),
                ("2MK", Buttons.MediumKick | Buttons.Down, Buttons.Right | Buttons.Down),
                ("2MK", Buttons.MediumKick | Buttons.Down, Buttons.Right),
            }),

            Throws(),
        };

        // One pose sheet per move: `sheet-5LP`, `sheet-j.HK`, ...
        foreach (var move in Sim.Characters.Characters.Placeholder.Moves)
            all.Add(new Scenario($"sheet-{move.Input}", 1, _ => default, Captures: new[] { 1 }, Frames: 1, PoseSheet: move.Input));
        return all;
    }

    // Every ground normal, one every 40 frames, at point-blank against a standing P2, captured on its
    // first active frame with boxes on. The fighters are reset before each one, so pushback doesn't add up.
    private const int NormalPeriod = 40;

    private static readonly (string Input, Buttons Press, Buttons Defence)[] GroundNormals =
    {
        ("5LP", Buttons.LightPunch, default), ("5MP", Buttons.MediumPunch, default), ("5HP", Buttons.HeavyPunch, default),
        ("5LK", Buttons.LightKick, default), ("5MK", Buttons.MediumKick, default), ("5HK", Buttons.HeavyKick, default),
        ("2LP", Buttons.LightPunch | Buttons.Down, default), ("2MP", Buttons.MediumPunch | Buttons.Down, default), ("2HP", Buttons.HeavyPunch | Buttons.Down, default),
        ("2LK", Buttons.LightKick | Buttons.Down, default), ("2MK", Buttons.MediumKick | Buttons.Down, default), ("2HK", Buttons.HeavyKick | Buttons.Down, default),
    };

    // P1 does each move in turn at point-blank while P2 holds `Defence`, captured on the move's first
    // active frame. The fighters are reset before each one, so pushback doesn't add up.
    private static Scenario Sequence(string name, (string Input, Buttons Press, Buttons Defence)[] steps)
    {
        var character = Sim.Characters.Characters.Placeholder;
        var captures = new int[steps.Length];
        for (var k = 0; k < steps.Length; k++)
            captures[k] = k * NormalPeriod + character.FindMove(steps[k].Input)!.Startup + 1;

        FrameInput Script(int frame)
        {
            var k = frame / NormalPeriod;
            if (k >= steps.Length)
                return default;
            var press = steps[k].Press;
            var crouch = press & Buttons.Down;
            return new FrameInput(frame % NormalPeriod == 1 ? press : crouch, steps[k].Defence);
        }

        GameState Reset(GameState s)
        {
            if (s.Frame % NormalPeriod != 0)
                return s;
            var fresh = GameState.NewMatch(1);
            s.Fighters[0] = fresh.Fighters[0];
            s.Fighters[1] = fresh.Fighters[1];
            s.Fighters[0].Position = new FixVec2(Fix.FromInt(610), s.Fighters[0].Position.Y);
            s.Fighters[1].Position = new FixVec2(Fix.FromInt(670), s.Fighters[1].Position.Y);
            return s;
        }

        return new Scenario(name, 1, Script, captures, steps.Length * NormalPeriod, Boxes: true, EachFrame: Reset);
    }

    // Throws, boxes on, one every ThrowPeriod frames from a reset. P1 presses LP+LK on frame 1 of
    // each; it connects on frame 3.
    //  - A throw at point-blank: held (6), then P2 knocked down in front (14).
    //  - The same throw teched: P2 presses LP+LK three frames into the grab, and on the seventh
    //    they break apart (72).
    //  - A throw from out of range: reaching on its active frame (123), whiffed in recovery (132).
    private const int ThrowPeriod = 60;

    private static Scenario Throws()
    {
        const Buttons lpLk = Buttons.LightPunch | Buttons.LightKick;
        int[] gaps = { 60, 60, 150 };

        FrameInput Script(int frame)
        {
            var k = frame / ThrowPeriod;
            var f = frame % ThrowPeriod;
            var p1 = f == 1 && k < gaps.Length ? lpLk : default;
            var p2 = k == 1 && f == 6 ? lpLk : default;
            return new FrameInput(p1, p2);
        }

        GameState Reset(GameState s)
        {
            var k = s.Frame / ThrowPeriod;
            if (s.Frame % ThrowPeriod != 0 || k >= gaps.Length)
                return s;
            var fresh = GameState.NewMatch(1);
            s.Fighters[0] = fresh.Fighters[0];
            s.Fighters[1] = fresh.Fighters[1];
            s.Fighters[0].Position = new FixVec2(Fix.FromInt(610), s.Fighters[0].Position.Y);
            s.Fighters[1].Position = new FixVec2(Fix.FromInt(610 + gaps[k]), s.Fighters[1].Position.Y);
            return s;
        }

        return new Scenario("throws", 1, Script, new[] { 6, 14, 72, 123, 132 }, gaps.Length * ThrowPeriod,
            Boxes: true, EachFrame: Reset);
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

        // The stage bounds, boxes on. Both walk back until the separation limit stops them, pushboxes
        // spanning one screen (110); then P2 walks P1 into the left corner (380); then P2 walks back
        // and stops at the limit from the cornered P1 (660).
        new Scenario("stage-bounds", 1,
            f => f < 120 ? new FrameInput(Buttons.Left, Buttons.Right)
                : f < 400 ? new FrameInput(Buttons.Left, Buttons.Left)
                : new FrameInput(Buttons.Left, Buttons.Right),
            Captures: new[] { 110, 380, 660 }, Frames: 660, Boxes: true),

        // The camera, boxes on (the ruler under the floor shows where on the stage the view is).
        // P1 walks P2 right: the camera follows across the stage (60) and stops at the right wall
        // (260). Then P2 walks P1 all the way left (500) and into the left corner (700). The
        // frame and checksum stay fixed on screen throughout.
        new Scenario("camera", 1,
            f => f < 260 ? new FrameInput(Buttons.Right, Buttons.Right) : new FrameInput(Buttons.Left, Buttons.Left),
            Captures: new[] { 60, 260, 500, 700 }, Frames: 700, Boxes: true),

        // The frame-time overlay, with fixed numbers so the image is stable.
        new Scenario("overlay", 1, _ => WalkIn, Captures: new[] { 60 }, Frames: 60, Overlay: true),

        // Ten seconds of jittered 60 Hz frames through the real _Process path: every frame must run
        // exactly one tick, or walking would stutter.
        new Scenario("pacing", 1, f => f % 120 < 60 ? WalkIn : default, Captures: Array.Empty<int>(), Frames: 600,
            PacingCheck: true),

        // Style frames: mock screens in the style guide's palette, type and shapes, drawn over the
        // real fighters. Placeholder data, no behavior; they're the look the screens are built to.
        new Scenario("style-hud", 1, _ => default, Captures: new[] { 30 }, Frames: 30,
            Setup: s => Place(s, 500, 780), StyleFrame: () => new HudFrame()),
        new Scenario("style-title", 1, _ => default, Captures: new[] { 30 }, Frames: 30,
            Setup: s => Place(s, 380, 900), StyleFrame: () => new TitleFrame()),
        new Scenario("style-select", 1, _ => default, Captures: new[] { 30 }, Frames: 30,
            Setup: s => Place(s, 300, 980), StyleFrame: () => new SelectFrame()),
        // The loser is down (crouching, until knockdowns exist).
        new Scenario("style-results", 1, _ => new FrameInput(Buttons.None, Buttons.Down), Captures: new[] { 30 },
            Frames: 30, Setup: s => Place(s, 260, 1020), StyleFrame: () => new ResultsFrame()),
    };

    // Puts the fighters at these screen x positions. The view centers on the fighters, so a pair
    // placed symmetrically about the stage center lands where the style frame expects them.
    private static GameState Place(GameState s, int x1, int x2)
    {
        var offset = s.Stage.Center - Fix.FromInt(Stage.ScreenWidth / 2);
        s.Fighters[0].Position = new FixVec2(Fix.FromInt(x1) + offset, s.Fighters[0].Position.Y);
        s.Fighters[1].Position = new FixVec2(Fix.FromInt(x2) + offset, s.Fighters[1].Position.Y);
        return s;
    }

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
