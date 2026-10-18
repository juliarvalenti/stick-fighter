// Headless replay runner.
//
//   dotnet run --project tools/ReplayRunner -- check replays/          every *.sfr must reproduce
//   dotnet run --project tools/ReplayRunner -- rerecord replays/       after a deliberate sim change
//   dotnet run --project tools/ReplayRunner -- generate out.sfr SEED FRAMES
//   dotnet run --project tools/ReplayRunner -- script out.sfr NAME      a scripted match, see Scripts
//
// check prints the first diverging frame of each failing replay and exits 1 if any fail.
// rerecord keeps each replay's inputs and rewrites its checksums; review the diff, since this is
// how an intended behavior change gets accepted.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using StickFighter.Sim;
using StickFighter.Sim.Numerics;
using StickFighter.Sim.Replays;

if (args.Length < 2)
    return Usage();

switch (args[0])
{
    case "check":
        return Check(args[1]);
    case "rerecord":
        return Rerecord(args[1]);
    case "generate" when args.Length == 4:
        return Write(args[1], Replay.Record(ulong.Parse(args[2]), Stage.Training, Walkabout(ulong.Parse(args[2]), int.Parse(args[3]))));
    case "script" when args.Length == 3 && Scripts(args[2]) is { } script:
        return Write(args[1], Replay.Record(1, Stage.Training, script));
    default:
        return Usage();
}

static int Check(string folder)
{
    var files = Files(folder);
    if (files.Length == 0)
    {
        Console.WriteLine($"FAIL no replays in {folder}");
        return 1;
    }

    var failures = 0;
    foreach (var file in files)
    {
        Replay replay;
        try
        {
            replay = Replay.Read(File.ReadAllText(file));
        }
        catch (FormatException e)
        {
            failures++;
            Console.WriteLine($"FAIL {file}  {e.Message}");
            continue;
        }

        if (!replay.MatchesLoadedData)
        {
            failures++;
            Console.WriteLine($"FAIL {file}  recorded with character data {replay.DataHash:x16}, but this build has {Replay.LoadedDataHash:x16}");
            Console.WriteLine("     if the data change is intended, rerecord and say why in the PR");
            continue;
        }

        var frame = replay.FirstDivergence(out var expected, out var actual);
        if (frame < 0)
        {
            Console.WriteLine($"ok   {file}  {replay.Frames} frames  final {replay.FinalChecksum:x16}");
            continue;
        }
        failures++;
        Console.WriteLine($"FAIL {file}  diverges at frame {frame}: expected {expected:x16}, got {actual:x16}");
        Console.WriteLine($"     inputs on that frame: p1 {replay.Inputs[frame].Player1}, p2 {replay.Inputs[frame].Player2}");
    }
    return failures == 0 ? 0 : 1;
}

static int Rerecord(string folder)
{
    foreach (var file in Files(folder))
    {
        var old = Replay.Read(File.ReadAllText(file), allowOldVersions: true);
        var updated = Replay.Record(old.Seed, old.Stage, old.Inputs);
        File.WriteAllText(file, updated.Write());
        Console.WriteLine($"rerecorded {file}: final {old.FinalChecksum:x16} -> {updated.FinalChecksum:x16}");
    }
    return 0;
}

static string[] Files(string folder) =>
    Directory.GetFiles(folder, "*.sfr").OrderBy(f => f, StringComparer.Ordinal).ToArray();

// A scripted placeholder match: random holds of walking, crouching, jumping and attacks for both
// players, from a seeded RNG, so it covers the input layer, pushing, walls and facing.
static List<FrameInput> Walkabout(ulong seed, int frames)
{
    var rng = Rng.Create(seed, 7);
    var inputs = new List<FrameInput>(frames);
    Buttons p1 = Buttons.None, p2 = Buttons.None;
    while (inputs.Count < frames)
    {
        if (rng.Chance(1, 8)) p1 = RandomButtons(ref rng);
        if (rng.Chance(1, 8)) p2 = RandomButtons(ref rng);
        inputs.Add(new FrameInput(p1, p2));
    }
    return inputs;
}

static Buttons RandomButtons(ref Rng rng)
{
    var buttons = (Buttons)rng.NextInt(1 << 10);
    return rng.Chance(1, 2) ? buttons & Buttons.Directions : buttons;
}

// Scripted matches on the training stage, as runs of (p1, p2, frames). P1 starts on the left
// facing right and P2 on the right facing left, so Left is back for P1 and forward for P2.
static List<FrameInput>? Scripts(string name) => name switch
{
    // Both walk back to the separation limit and stop; then P1 alone keeps walking back from a
    // standing P2 (the moving invisible wall), and follows P2 as P2 walks back too.
    "stage-apart" => Runs(
        (Buttons.Left, Buttons.Right, 150),
        (Buttons.Left, Buttons.None, 60),
        (Buttons.Left, Buttons.Right, 60),
        (Buttons.None, Buttons.Left, 60),
        (Buttons.Left, Buttons.None, 60)),

    // Both walk left into the left wall, then right into the right wall.
    "stage-walls" => Runs(
        (Buttons.Left, Buttons.Left, 300),
        (Buttons.Right, Buttons.Right, 600)),

    // P2 corners P1, then backs off to the separation limit while P1 holds back in the corner,
    // then P1 walks out of the corner after P2.
    "stage-corner" => Runs(
        (Buttons.Left, Buttons.Left, 300),
        (Buttons.Left, Buttons.Right, 300),
        (Buttons.Right, Buttons.Right, 120)),

    // P1 walks up to a standing P2 and lands 5MP, then both stand while hitstun plays out.
    "hit" => Runs(
        (Buttons.Right, Buttons.None, 46),
        (Buttons.MediumPunch, Buttons.None, 1),
        (Buttons.None, Buttons.None, 40)),

    // P1 throws 5HK from the starting distance and nothing connects.
    "whiff" => Runs(
        (Buttons.HeavyKick, Buttons.None, 1),
        (Buttons.None, Buttons.None, 40)),

    // P1 walks P2 back into the right wall, then hits P2 there twice: P2 stays pinned and P1 takes
    // the pushback instead.
    "pushback-corner" => Runs(
        (Buttons.Right, Buttons.Right, 240),
        (Buttons.MediumPunch, Buttons.None, 1),
        (Buttons.None, Buttons.None, 30),
        (Buttons.LightPunch, Buttons.None, 1),
        (Buttons.None, Buttons.None, 30)),

    // P1 walks in and throws 5MP; P2 blocks it standing (holds back from the press on).
    "block-stand" => Runs(
        (Buttons.Right, Buttons.None, 46),
        (Buttons.MediumPunch, Buttons.Right, 1),
        (Buttons.None, Buttons.Right, 40)),

    // P1 walks in and throws the low 2MK; P2 blocks it crouching.
    "block-crouch-low" => Runs(
        (Buttons.Right, Buttons.None, 46),
        (Buttons.Down | Buttons.MediumKick, Buttons.Down | Buttons.Right, 1),
        (Buttons.Down, Buttons.Down | Buttons.Right, 40)),

    // The same low against a standing block: it hits.
    "low-hits-standing-block" => Runs(
        (Buttons.Right, Buttons.None, 46),
        (Buttons.Down | Buttons.MediumKick, Buttons.Right, 1),
        (Buttons.Down, Buttons.Right, 40)),

    // P2 walks back from a distance, then stops in block stance when P1's 5HK starts up nearby
    // (proximity guard), and blocks it.
    "proximity-guard" => Runs(
        (Buttons.Right, Buttons.None, 30),
        (Buttons.HeavyKick, Buttons.Right, 1),
        (Buttons.None, Buttons.Right, 40)),

    // Both walk in and jab on the same frame: both get hit.
    "trade" => Runs(
        (Buttons.Right, Buttons.Left, 23),
        (Buttons.LightPunch, Buttons.LightPunch, 1),
        (Buttons.None, Buttons.None, 30)),

    // P1 walks in and throws: P2 is held, then lands in front, knocked down, and gets up.
    "throw" => Runs(
        (Buttons.Right, Buttons.None, 46),
        (Buttons.LightPunch | Buttons.LightKick, Buttons.None, 1),
        (Buttons.None, Buttons.None, 60)),

    // Holding back while throwing: P2 lands behind P1, and the two swap sides.
    "throw-back" => Runs(
        (Buttons.Right, Buttons.None, 46),
        (Buttons.Left | Buttons.LightPunch | Buttons.LightKick, Buttons.None, 1),
        (Buttons.Left, Buttons.None, 10),
        (Buttons.None, Buttons.None, 50)),

    // P2 guards a jab (+3), and P1 throws on its first free frame. The throw's first active frame
    // is P2's last blockstun frame and doesn't grab; the second is P2's first free frame and does.
    "throw-meaty" => Runs(
        (Buttons.Right, Buttons.None, 46),
        (Buttons.LightPunch, Buttons.Right, 1),
        (Buttons.None, Buttons.Right, 10),
        (Buttons.LightPunch | Buttons.LightKick, Buttons.Right, 1),
        (Buttons.None, Buttons.Right, 60)),

    // The meaty throw, teched on the first frame of the window: P2 presses LP+LK four frames
    // before the grab, in blockstun, so P2's own throw comes out and is in startup when P1's lands.
    "throw-tech-first" => Runs(
        (Buttons.Right, Buttons.None, 46),
        (Buttons.LightPunch, Buttons.Right, 1),
        (Buttons.None, Buttons.Right, 9),
        (Buttons.None, Buttons.Right | Buttons.LightPunch | Buttons.LightKick, 1),
        (Buttons.LightPunch | Buttons.LightKick, Buttons.Right, 1),
        (Buttons.None, Buttons.Right, 40)),

    // The meaty throw, teched on the last frame of the window: seven frames into the grab.
    "throw-tech-last" => Runs(
        (Buttons.Right, Buttons.None, 46),
        (Buttons.LightPunch, Buttons.Right, 1),
        (Buttons.None, Buttons.Right, 10),
        (Buttons.LightPunch | Buttons.LightKick, Buttons.Right, 1),
        (Buttons.None, Buttons.Right, 9),
        (Buttons.None, Buttons.Right | Buttons.LightPunch | Buttons.LightKick, 1),
        (Buttons.None, Buttons.Right, 40)),

    // P1 throws from just out of range and whiffs; P2's standing heavy kick lands in the recovery.
    "throw-whiff-punished" => Runs(
        (Buttons.Right, Buttons.None, 39),
        (Buttons.LightPunch | Buttons.LightKick, Buttons.None, 1),
        (Buttons.None, Buttons.None, 3),
        (Buttons.None, Buttons.HeavyKick, 1),
        (Buttons.None, Buttons.None, 40)),

    // A jab hits (+6), and P1 throws on its first free frame: both active frames are hitstun, so
    // it whiffs. P2 takes only the jab.
    "throw-hitstun" => Runs(
        (Buttons.Right, Buttons.None, 46),
        (Buttons.LightPunch, Buttons.None, 1),
        (Buttons.None, Buttons.None, 10),
        (Buttons.LightPunch | Buttons.LightKick, Buttons.None, 1),
        (Buttons.None, Buttons.None, 40)),

    // P2 starts a standing heavy kick as P1 throws, and is grabbed in its startup: a counter-hit
    // throw. P2's LP+LK two frames into the grab is in the window but can't tech, and the throw lands.
    "throw-counterhit" => Runs(
        (Buttons.Right, Buttons.None, 46),
        (Buttons.LightPunch | Buttons.LightKick, Buttons.HeavyKick, 1),
        (Buttons.None, Buttons.None, 3),
        (Buttons.None, Buttons.LightPunch | Buttons.LightKick, 1),
        (Buttons.None, Buttons.None, 60)),

    // Both walk in and throw on the same frame: both tech and break apart.
    "throw-same-frame" => Runs(
        (Buttons.Right, Buttons.Left, 23),
        (Buttons.LightPunch | Buttons.LightKick, Buttons.LightPunch | Buttons.LightKick, 1),
        (Buttons.None, Buttons.None, 30)),

    _ => null,
};

static List<FrameInput> Runs(params (Buttons P1, Buttons P2, int Frames)[] runs)
{
    var inputs = new List<FrameInput>();
    foreach (var (p1, p2, frames) in runs)
    {
        for (var i = 0; i < frames; i++)
            inputs.Add(new FrameInput(p1, p2));
    }
    return inputs;
}

static int Write(string file, Replay replay)
{
    File.WriteAllText(file, replay.Write());
    Console.WriteLine($"wrote {file}: {replay.Frames} frames, final {replay.FinalChecksum:x16}");
    return 0;
}

static int Usage()
{
    Console.WriteLine("usage: ReplayRunner check|rerecord <folder> | generate <file> <seed> <frames> | script <file> <name>");
    return 2;
}
