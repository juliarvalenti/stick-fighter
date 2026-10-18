// Headless replay runner.
//
//   dotnet run --project tools/ReplayRunner -- check replays/          every *.sfr must reproduce
//   dotnet run --project tools/ReplayRunner -- synctest replays/       every frame survives rollback
//                                                (check and synctest also take one .sfr file)
//   dotnet run --project tools/ReplayRunner -- rerecord replays/       after a deliberate sim change
//   dotnet run --project tools/ReplayRunner -- trace FILE FROM TO       every frame, to diff two builds
//   dotnet run --project tools/ReplayRunner -- generate out.sfr SEED FRAMES
//   dotnet run --project tools/ReplayRunner -- script out.sfr NAME      a scripted match, see Scripts
//
// check prints, for each failing replay, the first checkpoint whose behavior differs and how (or
// that only the final state does), and exits 1 if any fail.
// synctest plays each replay's inputs, rolling back 1 to 8 frames after every frame (see SyncTest),
// and exits 1 if any resimulated frame comes out different, listing the GameState fields that differ.
// rerecord keeps each replay's inputs, rewrites its checkpoints and final checksum, and prints a
// line per replay saying whether behavior changed (and where and how) or only the encoding did. Paste
// that summary into the PR: it's how an intended behavior change gets reviewed and accepted.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using StickFighter.Sim;
using StickFighter.Sim.Characters;
using StickFighter.Sim.Numerics;
using StickFighter.Sim.Replays;

if (args.Length < 2)
    return Usage();

switch (args[0])
{
    case "check" when args.Length == 4 && args[2] == "--trace-out":
        return Check(args[1], args[3]);
    case "check":
        return Check(args[1], null);
    case "synctest":
        return SyncTestAll(args[1]);
    case "rerecord":
        return Rerecord(args[1]);
    case "trace" when args.Length == 4:
        return Trace(args[1], int.Parse(args[2]), int.Parse(args[3]));
    case "generate" when args.Length == 4:
        return Write(args[1], Replay.Record(ulong.Parse(args[2]), Stage.Training, Walkabout(ulong.Parse(args[2]), int.Parse(args[3]))));
    // Scripts named match-… are versus matches (rounds, timer, KO); the rest are training.
    case "script" when args.Length == 3 && Scripts(args[2]) is { } script:
        return Write(args[1], Replay.Record(1, Stage.Training, script,
            args[2].StartsWith("match-", StringComparison.Ordinal) ? MatchMode.Versus : MatchMode.Training));
    default:
        return Usage();
}

// traceOut: for each replay whose behavior differs, write <name>.range ("file from to") and this
// build's trace of that range as <name>.head.trace, so CI can trace the base build and diff.
static int Check(string folder, string? traceOut)
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

        if (replay.Verify() is not { } mismatch)
        {
            Console.WriteLine($"ok   {file}  {replay.Frames} frames  final {replay.FinalChecksum:x16}");
            continue;
        }
        failures++;
        if (mismatch.Frame < 0)
        {
            Console.WriteLine($"FAIL {file}  every checkpoint's behavior matches, but the final state differs ({mismatch.Differences[0]})");
            Console.WriteLine("     a field changed without changing what players see: if intended, rerecord");
            continue;
        }
        var previous = mismatch.Frame - Replay.CheckpointInterval + 1;
        Console.WriteLine($"FAIL {file}  behavior differs at checkpoint frame {mismatch.Frame}:");
        foreach (var difference in mismatch.Differences)
            Console.WriteLine($"       {difference}");
        var from = previous > 0 ? previous : 0;
        Console.WriteLine($"     it started between frames {from} and {mismatch.Frame}; to find the frame, trace that range on both builds and diff:");
        Console.WriteLine($"       ReplayRunner trace {file} {from} {mismatch.Frame}");
        if (traceOut is not null)
        {
            Directory.CreateDirectory(traceOut);
            var name = Path.GetFileNameWithoutExtension(file);
            File.WriteAllText(Path.Combine(traceOut, name + ".range"), $"{file} {from} {mismatch.Frame}\n");
            File.WriteAllLines(Path.Combine(traceOut, name + ".head.trace"), replay.Trace(from, mismatch.Frame));
        }
    }
    return failures == 0 ? 0 : 1;
}

// Every frame in a range, behavior and checksum, for diffing two builds.
static int Trace(string file, int from, int to)
{
    var replay = Replay.Read(File.ReadAllText(file));
    foreach (var line in replay.Trace(from, to))
        Console.WriteLine(line);
    return 0;
}

static int SyncTestAll(string folder)
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
        var replay = Replay.Read(File.ReadAllText(file));
        if (SyncTest.Run(replay.Seed, replay.Stage, replay.Inputs, replay.Mode) is { } failure)
        {
            failures++;
            Console.WriteLine($"FAIL {file}  {failure}");
            Console.WriteLine($"     inputs on that frame: p1 {replay.Inputs[failure.Frame].Player1}, p2 {replay.Inputs[failure.Frame].Player2}");
            continue;
        }
        Console.WriteLine($"ok   {file}  {replay.Frames} frames, rolled back 1-{SyncTest.MaxRollback} after each");
    }
    return failures == 0 ? 0 : 1;
}

// Rewrites each replay from its inputs and says what changed, for the PR body: nothing, the
// encoding only (behavior identical at every checkpoint), or behavior, from which frame and how.
static int Rerecord(string folder)
{
    foreach (var file in Files(folder))
    {
        var old = Replay.Read(File.ReadAllText(file), allowOldVersions: true);
        var updated = Replay.Record(old.Seed, old.Stage, old.Inputs, old.Mode);
        File.WriteAllText(file, updated.Write());

        var name = Path.GetFileNameWithoutExtension(file);
        if (old.Checkpoints.Count == 0)
            Console.WriteLine($"{name}: upgraded to format {Replay.FormatVersion} (no behavior recorded before)");
        else if (old.DataHash != updated.DataHash && old.Compare(updated) is null)
            Console.WriteLine($"{name}: character data changed, behavior identical");
        else if (old.Compare(updated) is not { } change)
            Console.WriteLine($"{name}: unchanged");
        else if (change.Frame < 0)
            Console.WriteLine($"{name}: encoding changed, behavior identical");
        else
            Console.WriteLine($"{name}: behavior changed at frame {change.Frame}: {string.Join("; ", change.Differences)}");
    }
    return 0;
}

// Every replay in a folder, or the one replay named.
static string[] Files(string path) =>
    File.Exists(path)
        ? new[] { path }
        : Directory.GetFiles(path, "*.sfr").OrderBy(f => f, StringComparer.Ordinal).ToArray();

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

    // Dashes: tap, release, tap. P1 dashes forward (66), then back (44).
    "dash-forward" => Runs(
        (Buttons.Right, Buttons.None, 1),
        (Buttons.None, Buttons.None, 2),
        (Buttons.Right, Buttons.None, 1),
        (Buttons.None, Buttons.None, 30)),

    "dash-back" => Runs(
        (Buttons.Left, Buttons.None, 1),
        (Buttons.None, Buttons.None, 2),
        (Buttons.Left, Buttons.None, 1),
        (Buttons.None, Buttons.None, 40)),

    // P2 corners P1 and steps back; P1 back-dashes into the wall (and stays there), then dashes out.
    "dash-wall" => Runs(
        (Buttons.Left, Buttons.Left, 300),
        (Buttons.None, Buttons.Right, 20),
        (Buttons.Left, Buttons.None, 1),
        (Buttons.None, Buttons.None, 2),
        (Buttons.Left, Buttons.None, 1),
        (Buttons.None, Buttons.None, 30),
        (Buttons.Right, Buttons.None, 1),
        (Buttons.None, Buttons.None, 2),
        (Buttons.Right, Buttons.None, 1),
        (Buttons.None, Buttons.None, 30)),

    // P2 corners P1 and throws 5HK at point-blank; P1 back-dashes into the wall so its invincible
    // frames cover all of the kick's active frames: with nowhere to go, invincibility alone is
    // what makes it whiff.
    "backdash-through-attack" => Runs(
        (Buttons.Left, Buttons.Left, 300),
        (Buttons.None, Buttons.None, 5),
        (Buttons.None, Buttons.HeavyKick, 1),
        (Buttons.None, Buttons.None, 2),
        (Buttons.Left, Buttons.None, 1),
        (Buttons.None, Buttons.None, 1),
        (Buttons.Left, Buttons.None, 1),
        (Buttons.None, Buttons.None, 40)),

    // Jumps. A jump is 4 pre-jump frames then takeoff (5 frames of up), 36 airborne, 4 landing.
    // P1 jumps straight up, then forward, then back, landing between each.
    "jump-three" => Runs(
        (Buttons.Up, Buttons.None, 5),
        (Buttons.None, Buttons.None, 45),
        (Buttons.Up | Buttons.Right, Buttons.None, 5),
        (Buttons.None, Buttons.None, 45),
        (Buttons.Up | Buttons.Left, Buttons.None, 5),
        (Buttons.None, Buttons.None, 45)),

    // P1 walks up to P2 and jumps over; P2 throws 5HK as P1 passes overhead, so P2 keeps facing
    // left until the kick ends while P1 turns on landing.
    "jump-over" => Runs(
        (Buttons.Right, Buttons.None, 46),
        (Buttons.Up | Buttons.Right, Buttons.None, 5),
        (Buttons.None, Buttons.None, 14),
        (Buttons.None, Buttons.HeavyKick, 1),
        (Buttons.None, Buttons.None, 60)),

    // An empty jump lands free: P1 walks on the first landing frame. Then a late air attack: the
    // landing has recovery, and P1 holding back guards only once it's over.
    "jump-landings" => Runs(
        (Buttons.Up, Buttons.None, 5),
        (Buttons.None, Buttons.None, 35),
        (Buttons.Right, Buttons.None, 10),
        (Buttons.Up, Buttons.None, 5),
        (Buttons.None, Buttons.None, 20),
        (Buttons.HeavyKick, Buttons.None, 1),
        (Buttons.Left, Buttons.None, 30)),

    // Owed from Blocking. A jump-in (high) blocked by a standing guard: P1 walks in, jumps forward and
    // kicks on the way down; P2 holds back from the kick on.
    "jump-in-blocked" => Runs(
        (Buttons.Right, Buttons.None, 20),
        (Buttons.Up | Buttons.Right, Buttons.None, 5),
        (Buttons.None, Buttons.None, 16),
        (Buttons.HeavyKick, Buttons.Right, 1),
        (Buttons.None, Buttons.Right, 40)),

    // The same jump-in against a crouching guard: it's high, so it hits.
    "jump-in-crouch-hit" => Runs(
        (Buttons.Right, Buttons.None, 10),
        (Buttons.Up | Buttons.Right, Buttons.None, 5),
        (Buttons.None, Buttons.None, 20),
        (Buttons.HeavyKick, Buttons.Right | Buttons.Down, 1),
        (Buttons.None, Buttons.Right | Buttons.Down, 40)),

    // A crossup: P1 walks in, jumps forward over P2 and drives j.LK's foot down on P2's head from
    // just past it. P2 holds away from where P1 is (Left, toward its old facing) and blocks...
    "crossup-blocked" => Runs(
        (Buttons.Right, Buttons.None, 32),
        (Buttons.Up | Buttons.Right, Buttons.None, 5),
        (Buttons.None, Buttons.None, 21),
        (Buttons.LightKick, Buttons.Left, 1),
        (Buttons.None, Buttons.Left, 40)),

    // ...and holding back by its old facing (Right, toward P1) is hit.
    "crossup-hit" => Runs(
        (Buttons.Right, Buttons.None, 37),
        (Buttons.Up | Buttons.Right, Buttons.None, 5),
        (Buttons.None, Buttons.None, 21),
        (Buttons.LightKick, Buttons.Right, 1),
        (Buttons.None, Buttons.Right, 40)),

    // Getting hit in the air: P2 walks in and jumps straight up holding back (no air guard), P1
    // anti-airs with 5HP; P2 falls and lands with recovery.
    "air-hit" => Runs(
        (Buttons.None, Buttons.Left, 30),
        (Buttons.None, Buttons.Up, 5),
        (Buttons.None, Buttons.None, 20),
        (Buttons.HeavyPunch, Buttons.Right, 1),
        (Buttons.None, Buttons.Right, 60)),

    // Ground movement. P1 walks into a standing P2 and pushes it along at the push share; then P2
    // walks in too and both stop; then P1 backs off and P2 follows.
    "ground-walk-in" => Runs(
        (Buttons.Right, Buttons.None, 120),
        (Buttons.Right, Buttons.Left, 30),
        (Buttons.Left, Buttons.Left, 60)),

    // P1 crouches through the transition, holds it, throws 2LP and stays down, stands, then
    // crouches down-back and down-forward (neither walks); P2 crouches and stands in between.
    "ground-crouch" => Runs(
        (Buttons.Down, Buttons.None, 20),
        (Buttons.Down | Buttons.LightPunch, Buttons.Down, 1),
        (Buttons.Down, Buttons.Down, 20),
        (Buttons.None, Buttons.None, 10),
        (Buttons.Down | Buttons.Left, Buttons.None, 15),
        (Buttons.Down | Buttons.Right, Buttons.Down | Buttons.Right, 15),
        (Buttons.None, Buttons.None, 10)),

    // P1 walks in and P2 backs off, then the other way round, then both back off to the limit.
    "ground-back-off" => Runs(
        (Buttons.Right, Buttons.Right, 60),
        (Buttons.Left, Buttons.Left, 60),
        (Buttons.Left, Buttons.Right, 150)),

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

    // P1 walks in and throws as P2 starts a jump: the throw is active during P2's pre-jump frames,
    // which can't be thrown, so it whiffs and P2 jumps.
    "throw-prejump" => Runs(
        (Buttons.Right, Buttons.None, 46),
        (Buttons.LightPunch | Buttons.LightKick, Buttons.Up, 1),
        (Buttons.None, Buttons.Up, 4),
        (Buttons.None, Buttons.None, 60)),

    // P2 jumps a little earlier, so it has just left the ground when the throw is active: whiff.
    "throw-airborne" => Runs(
        (Buttons.Right, Buttons.None, 40),
        (Buttons.Right, Buttons.Up, 5),
        (Buttons.Right, Buttons.None, 1),
        (Buttons.LightPunch | Buttons.LightKick, Buttons.None, 1),
        (Buttons.None, Buttons.None, 60)),

    // P2 jumps straight up and P1 walks in and throws it in its landing frames; landing can be
    // teched, and P2 techs.
    "throw-landing-tech" => Runs(
        (Buttons.Right, Buttons.Up, 5),
        (Buttons.Right, Buttons.None, 35),
        (Buttons.LightPunch | Buttons.LightKick, Buttons.None, 1),
        (Buttons.None, Buttons.None, 4),
        (Buttons.None, Buttons.LightPunch | Buttons.LightKick, 1),
        (Buttons.None, Buttons.None, 50)),

    // Holding back while throwing: P2 lands behind P1, and the two swap sides.
    "throw-back" => Runs(
        (Buttons.Right, Buttons.None, 46),
        (Buttons.Left | Buttons.LightPunch | Buttons.LightKick, Buttons.None, 1),
        (Buttons.Left, Buttons.None, 10),
        (Buttons.None, Buttons.None, 50)),

    // P2 guards a jab (+3), and P1 throws on its first free frame. The throw's first active frame
    // is P2's last blockstun frame and doesn't grab; the second is P2's first free frame and does.
    // (Waits after a jab include its hitstop: 6 frames on block, 8 on hit.)
    "throw-meaty" => Runs(
        (Buttons.Right, Buttons.None, 46),
        (Buttons.LightPunch, Buttons.Right, 1),
        (Buttons.None, Buttons.Right, 16),
        (Buttons.LightPunch | Buttons.LightKick, Buttons.Right, 1),
        (Buttons.None, Buttons.Right, 60)),

    // The meaty throw, teched on the first frame of the window: P2 presses LP+LK four frames
    // before the grab, in blockstun, so P2's own throw comes out and is in startup when P1's lands.
    "throw-tech-first" => Runs(
        (Buttons.Right, Buttons.None, 46),
        (Buttons.LightPunch, Buttons.Right, 1),
        (Buttons.None, Buttons.Right, 15),
        (Buttons.None, Buttons.Right | Buttons.LightPunch | Buttons.LightKick, 1),
        (Buttons.LightPunch | Buttons.LightKick, Buttons.Right, 1),
        (Buttons.None, Buttons.Right, 40)),

    // The meaty throw, teched on the last frame of the window: seven frames into the grab.
    "throw-tech-last" => Runs(
        (Buttons.Right, Buttons.None, 46),
        (Buttons.LightPunch, Buttons.Right, 1),
        (Buttons.None, Buttons.Right, 16),
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
        (Buttons.None, Buttons.None, 18),
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

    // P1 jabs P2 (+6 on hit), then plinks the next jab in its recovery: LP, then MP a frame later
    // with LP held. The earliest press comes out, so the jab links: two hits, one combo.
    "plink-link" => Runs(
        (Buttons.Right, Buttons.None, 46),
        (Buttons.LightPunch, Buttons.None, 1),
        (Buttons.None, Buttons.None, 16),
        (Buttons.LightPunch, Buttons.None, 1),
        (Buttons.LightPunch | Buttons.MediumPunch, Buttons.None, 1),
        (Buttons.None, Buttons.None, 30)),

    // Hit states. P2 starts a heavy kick as P1 jabs: the jab lands in the kick's startup, a counter hit.
    "counter-hit" => Runs(
        (Buttons.Right, Buttons.None, 46),
        (Buttons.LightPunch, Buttons.HeavyKick, 1),
        (Buttons.None, Buttons.None, 40)),

    // Both walk right until P2 is in the corner, then P1 mashes jab (pressed every other frame) for
    // 40 frames: each comes out on the first free frame and links (+6), so three hits make one combo,
    // scaled 100%, 100%, 80%.
    "combo-scaling" => Concat(
        Runs((Buttons.Right, Buttons.Right, 300)),
        Mash(Buttons.LightPunch, Buttons.None, 40),
        Runs((Buttons.None, Buttons.None, 40))),

    // P2 crouches; P1's crouching jab gets the crouch hitstun bonus.
    "crouch-hitstun" => Runs(
        (Buttons.Right, Buttons.Down, 46),
        (Buttons.Down | Buttons.LightPunch, Buttons.Down, 1),
        (Buttons.Down, Buttons.Down, 30)),

    // P1 jumps straight up at point-blank and P2's jab catches it rising, knocking it into a fall;
    // P2 keeps mashing jab, but a normal can't juggle, so the next jab passes through the falling P1.
    "air-no-juggle" => Concat(
        Runs((Buttons.Right, Buttons.None, 46), (Buttons.Up, Buttons.None, 3), (Buttons.None, Buttons.LightPunch, 1)),
        Mash(Buttons.None, Buttons.LightPunch, 40),
        Runs((Buttons.None, Buttons.None, 20))),

    // Both walk in and throw on the same frame: both tech and break apart.
    "throw-same-frame" => Runs(
        (Buttons.Right, Buttons.Left, 23),
        (Buttons.LightPunch | Buttons.LightKick, Buttons.LightPunch | Buttons.LightKick, 1),
        (Buttons.None, Buttons.None, 30)),

    // Versus matches, best of three, played round by round (see Played).
    // P1 KOs P2 twice: 2-0.
    "match-ko-ko" => Played(Plan.P1Kos, Plan.P1Kos),

    // P1 lands one hit and then nobody does anything: P1 is ahead at time over. Then P1 KOs: 2-0.
    "match-time-over" => Played(Plan.P1AheadOnTime, Plan.P1Kos),

    // Both trade heavy punches from full health: a double KO, a round each (1-1). Then P2 KOs: 1-2.
    "match-double-ko" => Played(Plan.Trade, Plan.P2Kos),

    // A double KO (1-1), then a draw at time over (2-2), then a double KO in the final round: a
    // drawn match.
    "match-draw" => Played(Plan.Trade, Plan.Wait, Plan.Trade),

    // P1 wins one, P2 wins one, then a double KO: 2-2 and a final round, which P2 wins.
    "match-final-round" => Played(Plan.P1Kos, Plan.P2Kos, Plan.Trade, Plan.P2Kos),

    _ => null,
};

// A versus match played out against the live state, a plan per round, so a script needn't count
// frames to a KO; then a second of the result. The replay is just the inputs it came to.
static List<FrameInput> Played(params Plan[] rounds)
{
    var state = GameState.NewMatch(1, Stage.Training, MatchMode.Versus);
    var inputs = new List<FrameInput>();
    var after = 0;
    while (after < GameInfo.TicksPerSecond)
    {
        if (state.Match.Phase == MatchPhase.MatchEnd)
            after++;
        else if (state.Match.Round > rounds.Length)
            throw new InvalidOperationException($"the match went to round {state.Match.Round}, past the plans");
        var plan = rounds[Math.Min(state.Match.Round, rounds.Length) - 1];
        var input = new FrameInput(Plays(state, 0, plan), Plays(state, 1, plan));
        inputs.Add(input);
        Simulation.Step(ref state, input);
    }
    return inputs;
}

static Buttons Plays(in GameState state, int player, Plan plan)
{
    var full = Characters.Placeholder.Stats.Health;
    var attacking = plan switch
    {
        Plan.P1Kos => player == 0,
        Plan.P2Kos => player == 1,
        Plan.Trade => true,
        Plan.P1AheadOnTime => player == 0 && state.Fighters[1].Health == full,
        _ => false,
    };
    return attacking ? Attack(state, player) : Buttons.None;
}

// Walk in, and heavy punch from close whenever free to. Both doing it from the mirrored start
// trade on every hit.
static Buttons Attack(in GameState state, int player)
{
    var me = state.Fighters[player];
    var forward = me.Facing > 0 ? Buttons.Right : Buttons.Left;
    var gap = Fix.Abs(state.Fighters[1].Position.X - state.Fighters[0].Position.X);
    var free = me.Action is (int)FighterAction.Stand or (int)FighterAction.WalkForward;
    return free && gap <= Fix.FromInt(70) ? Buttons.HeavyPunch : forward;
}

// Buttons pressed every other frame (down, up, down...) for `frames` frames: mashing.
static List<FrameInput> Mash(Buttons p1, Buttons p2, int frames)
{
    var inputs = new List<FrameInput>();
    for (var i = 0; i < frames; i++)
        inputs.Add(i % 2 == 0 ? new FrameInput(p1, p2) : default);
    return inputs;
}

static List<FrameInput> Concat(params List<FrameInput>[] parts)
{
    var inputs = new List<FrameInput>();
    foreach (var part in parts)
        inputs.AddRange(part);
    return inputs;
}

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
    Console.WriteLine("usage: ReplayRunner check|synctest|rerecord <folder> | trace <file> <from> <to> | generate <file> <seed> <frames> | script <file> <name>");
    return 2;
}

// What each player does in a played round (see Played).
enum Plan { P1Kos, P2Kos, Trade, P1AheadOnTime, Wait }
