using System.Collections.Generic;
using Godot;
using StickFighter.Sim;

namespace StickFighter;

// Runs one scenario inside the real game loop and exits with 0 (pass) or 1 (fail).
//
//   godot --path . --rendering-method gl_compatibility --fixed-fps 60 -- --scenario walk-in
//         [--out <dir>] [--golden <dir>] [--update-goldens]
//
// Each capture renders the game's own viewport to <out>/<scenario>-<frame>.png; nobody's screen is
// read. With --golden it compares against <golden>/<scenario>-<frame>.png, and on a mismatch also
// writes .golden.png and .diff.png next to the capture. --update-goldens writes the goldens instead.
public partial class ScenarioRun : Node
{
    // A pixel counts as different when any channel is off by more than this (of 255). Software
    // rasterizers can disagree slightly along anti-aliased edges.
    private const int ChannelTolerance = 24;

    // How many such pixels a frame may have before it fails (out of 921,600).
    private const int MaxDifferentPixels = 200;

    private static readonly Vector2I CaptureSize = new(1280, 720);

    private readonly Scenario _scenario;
    private readonly string _outDir;
    private readonly string? _goldenDir;
    private readonly bool _updateGoldens;
    private readonly HashSet<int> _pending;
    private readonly List<int> _ticksPerFrame = new();
    private int _failures;
    private bool _finishing;

    public ScenarioRun(Scenario scenario, string outDir, string? goldenDir, bool updateGoldens)
    {
        _scenario = scenario;
        _outDir = ProjectSettings.GlobalizePath(outDir);
        _goldenDir = goldenDir is null ? null : ProjectSettings.GlobalizePath(goldenDir);
        _updateGoldens = updateGoldens;
        _pending = new HashSet<int>(scenario.Captures);
        DirAccess.MakeDirRecursiveAbsolute(_outDir);
        if (_updateGoldens && _goldenDir is not null)
            DirAccess.MakeDirRecursiveAbsolute(_goldenDir);
    }

    // Called by Main after each display frame's simulation step.
    public async void AfterStep(GameState state, int ticks)
    {
        if (_finishing)
            return;
        _ticksPerFrame.Add(ticks);

        if (_pending.Remove(state.Frame))
        {
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            Capture(state.Frame);
        }

        if (state.Frame >= _scenario.Frames && _pending.Count == 0)
            Finish();
    }

    private void Capture(int frame)
    {
        var image = GetViewport().GetTexture().GetImage();
        image.Convert(Image.Format.Rgba8);
        if (image.GetSize() != CaptureSize)
            image.Resize(CaptureSize.X, CaptureSize.Y, Image.Interpolation.Bilinear);

        var name = $"{_scenario.Name}-{frame}";
        var actualPath = $"{_outDir}/{name}.png";
        image.SavePng(actualPath);
        GD.Print($"captured {actualPath}");

        if (_goldenDir is null)
            return;
        var goldenPath = $"{_goldenDir}/{name}.png";

        if (_updateGoldens)
        {
            image.SavePng(goldenPath);
            GD.Print($"updated golden {goldenPath}");
            return;
        }

        if (!FileAccess.FileExists(goldenPath))
        {
            Fail($"{name}: no golden at {goldenPath} (run tools/update-goldens.sh to add it)");
            return;
        }

        var golden = Image.LoadFromFile(goldenPath);
        golden.Convert(Image.Format.Rgba8);
        golden.SavePng($"{_outDir}/{name}.golden.png");
        var different = Compare(image, golden, out var diff);
        if (different > MaxDifferentPixels)
        {
            diff.SavePng($"{_outDir}/{name}.diff.png");
            Fail($"{name}: {different} pixels differ from the golden (limit {MaxDifferentPixels}); see {name}.diff.png");
        }
        else
        {
            GD.Print($"ok   {name}: {different} pixels differ (limit {MaxDifferentPixels})");
        }
    }

    // Counts pixels whose largest channel difference exceeds the tolerance, and draws them in red
    // over a faded copy of the capture.
    private static int Compare(Image actual, Image golden, out Image diff)
    {
        var size = actual.GetSize();
        diff = Image.CreateEmpty(size.X, size.Y, false, Image.Format.Rgba8);
        if (golden.GetSize() != size)
        {
            diff.Fill(Colors.Red);
            return size.X * size.Y;
        }

        var a = actual.GetData();
        var g = golden.GetData();
        var d = new byte[a.Length];
        var count = 0;
        for (var i = 0; i < a.Length; i += 4)
        {
            var worst = 0;
            for (var c = 0; c < 3; c++)
                worst = Mathf.Max(worst, Mathf.Abs(a[i + c] - g[i + c]));

            if (worst > ChannelTolerance)
            {
                count++;
                d[i] = 255;
                d[i + 1] = 0;
                d[i + 2] = 0;
            }
            else
            {
                var gray = (byte)(160 + (a[i] + a[i + 1] + a[i + 2]) / 12);
                d[i] = d[i + 1] = d[i + 2] = gray;
            }
            d[i + 3] = 255;
        }
        diff.SetData(size.X, size.Y, false, Image.Format.Rgba8, d);
        return count;
    }

    private void Finish()
    {
        _finishing = true;

        if (_scenario.PacingCheck)
        {
            // The first frame may carry the driver's head start; every frame after must be one tick.
            for (var i = 1; i < _ticksPerFrame.Count; i++)
            {
                if (_ticksPerFrame[i] != 1)
                    Fail($"pacing: display frame {i} ran {_ticksPerFrame[i]} ticks");
            }
            if (_failures == 0)
                GD.Print($"ok   pacing: {_ticksPerFrame.Count} jittered 60 Hz frames, one tick each");
        }

        GD.Print(_failures == 0
            ? $"scenario {_scenario.Name}: pass"
            : $"scenario {_scenario.Name}: FAIL ({_failures})");
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    private void Fail(string message)
    {
        _failures++;
        GD.PrintErr($"FAIL {message}");
    }
}
