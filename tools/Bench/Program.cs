// Headless perf gate for the simulation.
//
//   dotnet run -c Release --project tools/Bench -- [--out result.json] [--baseline base.json]
//
// Two modes, each sampled per display frame:
//   tick     one save + one step, the steady-state cost per tick.
//   rollback one load + RollbackFrames resimulated ticks (save + step each), every frame.
// Fails when the p99 of either mode goes over budget, or when it regresses more than
// RegressionLimit against the baseline (ignoring anything under NoiseFloorUs).

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using StickFighter.Sim;

const int Frames = 60 * 60 * 3;           // a three-minute match
const int RollbackFrames = 8;
const double TickBudgetUs = 1000;         // step + save, from the quality gates task
const double RollbackBudgetUs = 10_000;   // 1 load + 9 × (step + save)
const double RegressionLimit = 1.20;
const double NoiseFloorUs = 50;           // below this, timer noise dominates
const double ModeTimeLimitUs = 15_000_000; // a slow step stops early with what it has, not hang CI

string? outPath = null, baselinePath = null;
for (var i = 0; i < args.Length; i++)
{
    if (args[i] == "--out") outPath = args[++i];
    else if (args[i] == "--baseline") baselinePath = args[++i];
}

var result = new Result(
    TickP99Us: Percentile(RunTick(), 0.99),
    RollbackP99Us: Percentile(RunRollback(), 0.99));

Console.WriteLine($"tick      p99 {result.TickP99Us,10:F2} us  (budget {TickBudgetUs} us)");
Console.WriteLine($"rollback  p99 {result.RollbackP99Us,10:F2} us  (budget {RollbackBudgetUs} us)");

if (outPath is not null)
    File.WriteAllText(outPath, JsonSerializer.Serialize(result));

var failures = 0;
failures += Check("tick", result.TickP99Us, TickBudgetUs);
failures += Check("rollback", result.RollbackP99Us, RollbackBudgetUs);

if (baselinePath is not null && File.Exists(baselinePath))
{
    var baseline = JsonSerializer.Deserialize<Result>(File.ReadAllText(baselinePath))!;
    failures += Regression("tick", result.TickP99Us, baseline.TickP99Us);
    failures += Regression("rollback", result.RollbackP99Us, baseline.RollbackP99Us);
}
else if (baselinePath is not null)
{
    Console.WriteLine($"no baseline at {baselinePath}; skipping the regression check");
}

return failures == 0 ? 0 : 1;

static double[] RunTick()
{
    var state = GameState.NewMatch(1);
    var saved = new GameState[RollbackFrames + 1];
    var samples = new double[Frames];
    var runStart = Stopwatch.GetTimestamp();
    Warmup(ref state, runStart);

    var frame = 0;
    for (; frame < Frames && !OutOfTime(runStart); frame++)
    {
        var start = Stopwatch.GetTimestamp();
        saved[frame % saved.Length] = state;
        Simulation.Step(ref state, Inputs.Walking);
        samples[frame] = Elapsed(start);
    }
    return samples[..Math.Max(frame, 1)];
}

static double[] RunRollback()
{
    var state = GameState.NewMatch(1);
    var saved = new GameState[RollbackFrames + 1];
    var samples = new double[Frames];
    var runStart = Stopwatch.GetTimestamp();
    Warmup(ref state, runStart);
    for (var i = 0; i < saved.Length; i++)
    {
        saved[i] = state;
        Simulation.Step(ref state, Inputs.Walking);
    }

    var frame = 0;
    for (; frame < Frames && !OutOfTime(runStart); frame++)
    {
        var start = Stopwatch.GetTimestamp();
        state = saved[frame % saved.Length];
        for (var i = 0; i <= RollbackFrames; i++)
        {
            saved[(frame + i) % saved.Length] = state;
            Simulation.Step(ref state, Inputs.Walking);
        }
        samples[frame] = Elapsed(start);
    }
    return samples[..Math.Max(frame, 1)];
}

static void Warmup(ref GameState state, long runStart)
{
    for (var i = 0; i < 1_000 && !OutOfTime(runStart); i++)
        Simulation.Step(ref state, Inputs.Walking);
}

static bool OutOfTime(long runStart) => Elapsed(runStart) > ModeTimeLimitUs;

static double Elapsed(long start) =>
    (Stopwatch.GetTimestamp() - start) * 1_000_000.0 / Stopwatch.Frequency;

static double Percentile(double[] samples, double p)
{
    var sorted = samples.OrderBy(s => s).ToArray();
    return sorted[(int)Math.Ceiling(p * sorted.Length) - 1];
}

static int Check(string mode, double p99, double budget)
{
    if (p99 <= budget) return 0;
    Console.WriteLine($"FAIL {mode}: p99 {p99:F2} us is over the {budget} us budget");
    return 1;
}

static int Regression(string mode, double p99, double baseline)
{
    if (p99 < NoiseFloorUs || p99 <= baseline * RegressionLimit) return 0;
    Console.WriteLine($"FAIL {mode}: p99 {p99:F2} us is {p99 / baseline:P0} of the baseline {baseline:F2} us");
    return 1;
}

record Result(double TickP99Us, double RollbackP99Us);

static class Inputs
{
    // Both fighters walking toward each other, so every step does real work.
    public static readonly FrameInput Walking = new(FrameInput.Right, FrameInput.Left);
}
