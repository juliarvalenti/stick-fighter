// Headless perf gate for the simulation.
//
//   dotnet run -c Release --project tools/Bench -- [--out result.json]
//   dotnet run -c Release --project tools/Bench -- compare --base b1.json b2.json … --head h1.json h2.json …
//
// Two modes, each sampled per display frame:
//   tick     one save + one step, the steady-state cost per tick.
//   rollback one load + RollbackFrames resimulated ticks (save + step each), every frame.
//
// A run fails when the p99 of either mode goes over budget: the worst frames are what a player
// feels, and the budgets are far above where we are.
//
// `compare` is the regression check. CI runs the base branch and this change in alternating
// rounds on the same runner (so drift on the machine hits both alike), each round writing its
// result, then compares the median over rounds of each round's median. A median is stable to
// a few percent between runs; a p99 is set by a handful of frames that hit a GC, a context
// switch or a JIT, which is why comparing p99s failed PRs that didn't touch the simulation. It
// fails when a mode is more than RegressionLimit slower and by more than MinDifferenceUs.
//
// Tiered compilation is off (Bench.csproj), so every frame times optimized code.

using System;
using System.Collections.Generic;
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
const double MinDifferenceUs = 0.1;       // smaller slowdowns are within the timer's resolution
const double ModeTimeLimitUs = 15_000_000; // a slow step stops early with what it has, not hang CI

if (args.Length > 0 && args[0] == "compare")
    return Compare(args);

string? outPath = null;
for (var i = 0; i < args.Length; i++)
{
    if (args[i] == "--out") outPath = args[++i];
}

var tick = RunTick();
var rollback = RunRollback();
var result = new Result(
    TickMedianUs: Percentile(tick, 0.5),
    TickP99Us: Percentile(tick, 0.99),
    RollbackMedianUs: Percentile(rollback, 0.5),
    RollbackP99Us: Percentile(rollback, 0.99));

Console.WriteLine($"tick      median {result.TickMedianUs,8:F2} us  p99 {result.TickP99Us,8:F2} us  (budget {TickBudgetUs} us)");
Console.WriteLine($"rollback  median {result.RollbackMedianUs,8:F2} us  p99 {result.RollbackP99Us,8:F2} us  (budget {RollbackBudgetUs} us)");

if (outPath is not null)
    File.WriteAllText(outPath, JsonSerializer.Serialize(result));

var failures = 0;
failures += Check("tick", result.TickP99Us, TickBudgetUs);
failures += Check("rollback", result.RollbackP99Us, RollbackBudgetUs);
return failures == 0 ? 0 : 1;

static int Compare(string[] args)
{
    var baseFiles = new List<string>();
    var headFiles = new List<string>();
    List<string>? into = null;
    foreach (var arg in args.Skip(1))
    {
        if (arg == "--base") into = baseFiles;
        else if (arg == "--head") into = headFiles;
        else if (into is not null) into.Add(arg);
    }
    if (baseFiles.Count == 0 || headFiles.Count == 0)
    {
        Console.WriteLine("usage: Bench compare --base <result.json>… --head <result.json>…");
        return 2;
    }

    var bases = baseFiles.Select(Load).ToArray();
    var heads = headFiles.Select(Load).ToArray();
    Console.WriteLine($"comparing {heads.Length} rounds of this change with {bases.Length} rounds of the base");

    var failures = 0;
    failures += Regression("tick", Median(heads.Select(r => r.TickMedianUs)), Median(bases.Select(r => r.TickMedianUs)));
    failures += Regression("rollback", Median(heads.Select(r => r.RollbackMedianUs)), Median(bases.Select(r => r.RollbackMedianUs)));
    return failures == 0 ? 0 : 1;
}

static Result Load(string path) => JsonSerializer.Deserialize<Result>(File.ReadAllText(path))!;

static double Median(IEnumerable<double> values) => Percentile(values.ToArray(), 0.5);

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

static int Regression(string mode, double head, double baseline)
{
    Console.WriteLine($"{mode,-9} median {head,8:F2} us vs base {baseline,8:F2} us ({head / baseline:P0})");
    if (head <= baseline * RegressionLimit || head - baseline <= MinDifferenceUs) return 0;
    Console.WriteLine($"FAIL {mode}: median {head:F2} us is {head / baseline:P0} of the base's {baseline:F2} us");
    return 1;
}

record Result(double TickMedianUs, double TickP99Us, double RollbackMedianUs, double RollbackP99Us);

static class Inputs
{
    // Both fighters walking toward each other, so every step does real work.
    public static readonly FrameInput Walking = new(Buttons.Right, Buttons.Left);
}
