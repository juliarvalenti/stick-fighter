// Headless replay runner.
//
//   dotnet run --project tools/ReplayRunner -- check replays/          every *.sfr must reproduce
//   dotnet run --project tools/ReplayRunner -- rerecord replays/       after a deliberate sim change
//   dotnet run --project tools/ReplayRunner -- generate out.sfr SEED FRAMES
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
        var replay = Replay.Record(ulong.Parse(args[2]), Walkabout(ulong.Parse(args[2]), int.Parse(args[3])));
        File.WriteAllText(args[1], replay.Write());
        Console.WriteLine($"wrote {args[1]}: {replay.Frames} frames, final {replay.FinalChecksum:x16}");
        return 0;
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
        var old = Replay.Read(File.ReadAllText(file), allowVersion1: true);
        var updated = Replay.Record(old.Seed, old.Inputs);
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

static int Usage()
{
    Console.WriteLine("usage: ReplayRunner check|rerecord <folder> | generate <file> <seed> <frames>");
    return 2;
}
