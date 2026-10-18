using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace StickFighter.Sim.Replays;

// A recorded match: the seed, the stage, both players' inputs for every frame, what the players
// could see at checkpoints along the way, and the full state checksum at the end. Replaying the
// inputs from the seed on that stage must reproduce every checkpoint and the final checksum;
// that's the determinism gate.
//
// Text format, one item per line so diffs read well:
//
//   stick-fighter-replay 6
//   seed 42
//   data 5be0c6f2a1d3e497    hash of the character data it was recorded with
//   stage 0 2560 600 1280    left wall, right wall, floor, max separation (whole pixels)
//   mode versus              sandbox (no rounds), versus (a best-of-three match) or training
//                            (sandbox with health refilling, the training mode the menu starts)
//   frames 87
//   input 0008 0004 2        p1 buttons, p2 buttons (hex), run length
//   input 0000 0000 85
//   at 59 p1 610 600 1 Stand 13 1000 p2 670 600 -1 Hitstun 2 940 match Fight 1 5340 0 0
//   at 86 p1 ...             the last frame is always a checkpoint
//   final 9f86d081884c7d65   checksum of the whole state after the last frame
//
// A checkpoint ("at") comes every CheckpointInterval frames and on the last frame. It holds each
// fighter's position, facing, action, action frame and health, and the match's phase, round, timer
// and wins: behavior, not encoding. So adding a field to the state changes one line per replay
// (the final checksum), while a change in behavior shows up as a readable diff on the checkpoint
// where it starts. The final checksum still covers every field, so a difference no checkpoint
// shows is still caught.
//
// Formats 1 to 4 stored a checksum per frame (1 had no data line, 2 no stage line, 3 no mode line).
// In formats 4 and 5 "mode training" meant what's now sandbox; 6 gave "training" to the training
// mode, so an older replay is never read with the new meaning. Older formats are read only to be
// rerecorded. A replay is only meaningful with the data it was recorded
// with, so one made with different move data is refused rather than reported as a divergence.
public sealed class Replay
{
    public const string Magic = "stick-fighter-replay";
    public const int FormatVersion = 6;
    public const int CheckpointInterval = 60;

    // A checkpoint's values, in order, by name; null marks a group label (p1, p2, match).
    private static readonly string?[] FieldNames =
    {
        null, "P1 x", "P1 y", "P1 facing", "P1 action", "P1 frame", "P1 health",
        null, "P2 x", "P2 y", "P2 facing", "P2 action", "P2 frame", "P2 health",
        null, "phase", "round", "timer", "P1 wins", "P2 wins",
    };

    public readonly record struct Checkpoint(int Frame, string[] Values)
    {
        public string Text => string.Join(' ', Values);
    }

    // Where a replay stopped matching: the frame, and each difference ("P2 health: 880 vs 860").
    // Frame is -1 when every checkpoint matched and only the final checksum differs.
    public readonly record struct Mismatch(int Frame, IReadOnlyList<string> Differences);

    public ulong Seed { get; }
    public ulong DataHash { get; }
    public Stage Stage { get; }
    public MatchMode Mode { get; }
    public IReadOnlyList<FrameInput> Inputs { get; }

    // Empty for a replay read from an old format, which recorded no behavior.
    public IReadOnlyList<Checkpoint> Checkpoints { get; }
    public ulong FinalChecksum { get; }

    public Replay(ulong seed, ulong dataHash, in Stage stage, IReadOnlyList<FrameInput> inputs, IReadOnlyList<Checkpoint> checkpoints, ulong finalChecksum,
        MatchMode mode = MatchMode.Sandbox)
    {
        Seed = seed;
        DataHash = dataHash;
        Stage = stage;
        Mode = mode;
        Inputs = inputs;
        Checkpoints = checkpoints;
        FinalChecksum = finalChecksum;
    }

    public int Frames => Inputs.Count;

    public static ulong LoadedDataHash => Characters.Characters.Placeholder.DataHash;

    // True if this build's character data is the data the replay was recorded with.
    public bool MatchesLoadedData => DataHash == LoadedDataHash;

    public static bool IsCheckpoint(int frame, int frames) =>
        (frame + 1) % CheckpointInterval == 0 || frame == frames - 1;

    // What the players can see after a frame, as checkpoint values (see FieldNames): for each
    // fighter, x, y, facing, action (by name), action frame and health; then the match's phase,
    // round, timer and wins.
    public static string[] Behavior(in GameState state)
    {
        var values = new List<string>();
        for (var p = 0; p < GameState.PlayerCount; p++)
        {
            ref readonly var f = ref state.Fighters[p];
            values.Add($"p{p + 1}");
            values.Add(f.Position.X.ToString());
            values.Add(f.Position.Y.ToString());
            values.Add(f.Facing.ToString(CultureInfo.InvariantCulture));
            values.Add(((Characters.FighterAction)f.Action).ToString());
            values.Add(f.ActionFrame.ToString(CultureInfo.InvariantCulture));
            values.Add(f.Health.ToString(CultureInfo.InvariantCulture));
        }
        values.Add("match");
        values.Add(state.Match.Phase.ToString());
        values.Add(state.Match.Round.ToString(CultureInfo.InvariantCulture));
        values.Add(state.Match.TimerFrames.ToString(CultureInfo.InvariantCulture));
        values.Add(state.Match.P1Wins.ToString(CultureInfo.InvariantCulture));
        values.Add(state.Match.P2Wins.ToString(CultureInfo.InvariantCulture));
        return values.ToArray();
    }

    // Plays the inputs from the seed and records the checkpoints and final checksum: how a replay is
    // made from inputs alone.
    public static Replay Record(ulong seed, in Stage stage, IReadOnlyList<FrameInput> inputs, MatchMode mode = MatchMode.Sandbox)
    {
        var state = GameState.NewMatch(seed, stage, mode);
        var checkpoints = new List<Checkpoint>();
        for (var i = 0; i < inputs.Count; i++)
        {
            Simulation.Step(ref state, inputs[i]);
            if (IsCheckpoint(i, inputs.Count))
                checkpoints.Add(new Checkpoint(i, Behavior(state)));
        }
        return new Replay(seed, LoadedDataHash, stage, inputs, checkpoints, state.Checksum(), mode);
    }

    // Replays the inputs and returns where they first stop matching, or null if everything does.
    public Mismatch? Verify() => Compare(Record(Seed, Stage, Inputs, Mode));

    // How `other`, played from the same inputs, differs from this replay: the first checkpoint whose
    // behavior differs, or (Frame -1) only the final checksum, or null for no difference.
    public Mismatch? Compare(Replay other)
    {
        var count = Checkpoints.Count < other.Checkpoints.Count ? Checkpoints.Count : other.Checkpoints.Count;
        for (var i = 0; i < count; i++)
        {
            var differences = Differences(Checkpoints[i].Values, other.Checkpoints[i].Values);
            if (differences.Count > 0)
                return new Mismatch(Checkpoints[i].Frame, differences);
        }
        if (FinalChecksum != other.FinalChecksum)
            return new Mismatch(-1, new[] { $"final checksum: {FinalChecksum:x16} vs {other.FinalChecksum:x16}" });
        return null;
    }

    // "P2 health: 880 vs 860" for each value that differs between two checkpoints.
    private static List<string> Differences(string[] expected, string[] actual)
    {
        var lines = new List<string>();
        for (var i = 0; i < expected.Length && i < actual.Length && i < FieldNames.Length; i++)
        {
            if (FieldNames[i] is { } name && expected[i] != actual[i])
                lines.Add($"{name}: {expected[i]} vs {actual[i]}");
        }
        return lines;
    }

    // Every frame from `from` to `to` (inclusive) as "frame N <behavior> <checksum>", to narrow a
    // divergence between checkpoints down to the frame: trace it on both builds and diff.
    public IEnumerable<string> Trace(int from, int to)
    {
        var state = GameState.NewMatch(Seed, Stage, Mode);
        for (var i = 0; i < Inputs.Count && i <= to; i++)
        {
            Simulation.Step(ref state, Inputs[i]);
            if (i >= from)
                yield return $"frame {i} {string.Join(' ', Behavior(state))} {state.Checksum():x16}";
        }
    }

    public string Write()
    {
        var text = new StringBuilder();
        text.Append(Magic).Append(' ').Append(FormatVersion).Append('\n');
        text.Append("seed ").Append(Seed.ToString(CultureInfo.InvariantCulture)).Append('\n');
        text.Append("data ").Append(DataHash.ToString("x16", CultureInfo.InvariantCulture)).Append('\n');
        text.Append("stage ")
            .Append(Pixels(Stage.LeftWall)).Append(' ')
            .Append(Pixels(Stage.RightWall)).Append(' ')
            .Append(Pixels(Stage.Floor)).Append(' ')
            .Append(Pixels(Stage.MaxSeparation)).Append('\n');
        text.Append("mode ").Append(ModeName(Mode)).Append('\n');
        text.Append("frames ").Append(Frames.ToString(CultureInfo.InvariantCulture)).Append('\n');

        for (var i = 0; i < Inputs.Count;)
        {
            var run = 1;
            while (i + run < Inputs.Count && Same(Inputs[i + run], Inputs[i]))
                run++;
            text.Append("input ")
                .Append(((ushort)Inputs[i].Player1).ToString("x4", CultureInfo.InvariantCulture)).Append(' ')
                .Append(((ushort)Inputs[i].Player2).ToString("x4", CultureInfo.InvariantCulture)).Append(' ')
                .Append(run.ToString(CultureInfo.InvariantCulture)).Append('\n');
            i += run;
        }

        foreach (var checkpoint in Checkpoints)
            text.Append("at ").Append(checkpoint.Frame.ToString(CultureInfo.InvariantCulture)).Append(' ').Append(checkpoint.Text).Append('\n');
        text.Append("final ").Append(FinalChecksum.ToString("x16", CultureInfo.InvariantCulture)).Append('\n');
        return text.ToString();
    }

    // Reads the current format. `allowOldVersions` lets rerecord upgrade formats 1 to 5: per-frame
    // checksums (1 to 4) are skipped, a version 1 data hash reads as 0, versions 1 and 2 were all
    // played on the training stage, 1 to 3 in sandbox mode, and 4 and 5's "training" is sandbox.
    public static Replay Read(string text, bool allowOldVersions = false)
    {
        using var reader = new StringReader(text);
        var header = Fields(reader.ReadLine(), 2, "header");
        if (header[0] != Magic)
            throw new FormatException("Not a stick-fighter replay");
        var version = int.Parse(header[1], CultureInfo.InvariantCulture);
        var perFrameChecksums = version is >= 1 and <= 4;
        var old = version is >= 1 and <= 5;
        if (old && !allowOldVersions)
        {
            throw new FormatException(perFrameChecksums
                ? $"Replay format {version} is from before checkpoints; rerecord it (ReplayRunner rerecord)"
                : $"Replay format {version}'s \"mode training\" meant what's now sandbox; rerecord it (ReplayRunner rerecord)");
        }
        if (version != FormatVersion && !old)
            throw new FormatException($"Replay format {header[1]}, but this build reads {FormatVersion}");

        var seed = ulong.Parse(Expect(reader, "seed"), CultureInfo.InvariantCulture);
        var dataHash = version == 1 ? 0 : ulong.Parse(Expect(reader, "data"), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var stage = version <= 2 ? Stage.Training : ReadStage(ExpectFields(reader, "stage", 5));
        var mode = version <= 3 ? MatchMode.Sandbox : version <= 5 ? ReadOldMode(Expect(reader, "mode")) : ReadMode(Expect(reader, "mode"));
        var frames = int.Parse(Expect(reader, "frames"), CultureInfo.InvariantCulture);
        var inputs = new List<FrameInput>(frames);
        var checkpoints = new List<Checkpoint>();
        ulong? final = null;

        for (var line = reader.ReadLine(); line is not null; line = reader.ReadLine())
        {
            if (line.Length == 0)
                continue;
            var f = line.Split(' ');
            switch (f[0])
            {
                case "input" when f.Length == 4:
                    var input = new FrameInput(
                        (Buttons)ushort.Parse(f[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                        (Buttons)ushort.Parse(f[2], NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                    var run = int.Parse(f[3], CultureInfo.InvariantCulture);
                    for (var i = 0; i < run; i++)
                        inputs.Add(input);
                    break;
                case "checksum" when perFrameChecksums && f.Length == 2:
                    break;
                case "at" when !perFrameChecksums && f.Length == 2 + FieldNames.Length:
                    checkpoints.Add(new Checkpoint(int.Parse(f[1], CultureInfo.InvariantCulture), f[2..]));
                    break;
                case "final" when f.Length == 2:
                    final = ulong.Parse(f[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                    break;
                default:
                    throw new FormatException($"Unexpected replay line: {line}");
            }
        }

        if (inputs.Count != frames)
            throw new FormatException($"Replay says {frames} frames but has {inputs.Count} inputs");
        if (final is null)
            throw new FormatException("Replay has no final checksum");
        if (!perFrameChecksums)
        {
            var expected = 0;
            for (var i = 0; i < frames; i++)
                expected += IsCheckpoint(i, frames) ? 1 : 0;
            if (checkpoints.Count != expected)
                throw new FormatException($"Replay has {checkpoints.Count} checkpoints, but {frames} frames need {expected}");
        }
        return new Replay(seed, dataHash, stage, inputs, checkpoints, final.Value, mode);
    }

    private static MatchMode ReadMode(string mode) => mode switch
    {
        "sandbox" => MatchMode.Sandbox,
        "versus" => MatchMode.Versus,
        "training" => MatchMode.Training,
        _ => throw new FormatException($"Unknown match mode '{mode}' (sandbox, versus or training)"),
    };

    // Formats 4 and 5, before sandbox had its name.
    private static MatchMode ReadOldMode(string mode) => mode switch
    {
        "training" => MatchMode.Sandbox,
        "versus" => MatchMode.Versus,
        _ => throw new FormatException($"Unknown match mode '{mode}' (training or versus)"),
    };

    private static string ModeName(MatchMode mode) => mode switch
    {
        MatchMode.Versus => "versus",
        MatchMode.Training => "training",
        _ => "sandbox",
    };

    private static bool Same(FrameInput a, FrameInput b) => a.Player1 == b.Player1 && a.Player2 == b.Player2;

    private static string Pixels(Numerics.Fix value) => value.FloorToInt().ToString(CultureInfo.InvariantCulture);

    private static Stage ReadStage(string[] f) => new(
        int.Parse(f[1], CultureInfo.InvariantCulture),
        int.Parse(f[2], CultureInfo.InvariantCulture),
        int.Parse(f[3], CultureInfo.InvariantCulture),
        int.Parse(f[4], CultureInfo.InvariantCulture));

    private static string[] Fields(string? line, int count, string what)
    {
        var fields = line?.Split(' ');
        if (fields is null || fields.Length != count)
            throw new FormatException($"Bad replay {what}: {line}");
        return fields;
    }

    private static string Expect(StringReader reader, string key) => ExpectFields(reader, key, 2)[1];

    private static string[] ExpectFields(StringReader reader, string key, int count)
    {
        var fields = Fields(reader.ReadLine(), count, key);
        if (fields[0] != key)
            throw new FormatException($"Expected '{key}', found '{fields[0]}'");
        return fields;
    }
}
