using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace StickFighter.Sim.Replays;

// A recorded match: the seed, the stage, both players' inputs for every frame, and the checksum
// after every frame. Replaying the inputs from the seed on that stage must reproduce every
// checksum; that's the determinism gate.
//
// Text format, one item per line so diffs read well:
//
//   stick-fighter-replay 4
//   seed 42
//   data 5be0c6f2a1d3e497    hash of the character data it was recorded with
//   stage 0 2560 600 1280    left wall, right wall, floor, max separation (whole pixels)
//   mode versus              training (no rounds) or versus (a best-of-three match)
//   frames 3
//   input 0008 0004 2        p1 buttons, p2 buttons (hex), run length
//   input 0000 0000 1
//   checksum 9f86d081884c7d65 (one per frame, after that frame)
//   ...
//   final 9f86d081884c7d65
//
// Version 1 had no data line, version 2 no stage line and version 3 no mode line. A replay is only
// meaningful with the data it was recorded with, so one made with different move data is refused
// rather than reported as a divergence. The stage and mode are recorded, so a replay always plays
// as it was recorded.
public sealed class Replay
{
    public const string Magic = "stick-fighter-replay";
    public const int FormatVersion = 4;

    public ulong Seed { get; }
    public ulong DataHash { get; }
    public Stage Stage { get; }
    public MatchMode Mode { get; }
    public IReadOnlyList<FrameInput> Inputs { get; }
    public IReadOnlyList<ulong> Checksums { get; }

    public Replay(ulong seed, ulong dataHash, in Stage stage, IReadOnlyList<FrameInput> inputs, IReadOnlyList<ulong> checksums,
        MatchMode mode = MatchMode.Training)
    {
        if (inputs.Count != checksums.Count)
            throw new ArgumentException("A replay needs one checksum per frame of input");
        Seed = seed;
        DataHash = dataHash;
        Stage = stage;
        Mode = mode;
        Inputs = inputs;
        Checksums = checksums;
    }

    public int Frames => Inputs.Count;

    public ulong FinalChecksum => Checksums.Count > 0 ? Checksums[^1] : GameState.NewMatch(Seed, Stage, Mode).Checksum();

    public static ulong LoadedDataHash => Characters.Characters.Placeholder.DataHash;

    // True if this build's character data is the data the replay was recorded with.
    public bool MatchesLoadedData => DataHash == LoadedDataHash;

    // Plays the inputs from the seed and records the checksums: how a replay is made from inputs alone.
    public static Replay Record(ulong seed, in Stage stage, IReadOnlyList<FrameInput> inputs, MatchMode mode = MatchMode.Training)
    {
        var state = GameState.NewMatch(seed, stage, mode);
        var checksums = new ulong[inputs.Count];
        for (var i = 0; i < inputs.Count; i++)
        {
            Simulation.Step(ref state, inputs[i]);
            checksums[i] = state.Checksum();
        }
        return new Replay(seed, LoadedDataHash, stage, inputs, checksums, mode);
    }

    // Replays the inputs and returns the first frame whose checksum differs, or -1 if all match.
    public int FirstDivergence(out ulong expected, out ulong actual)
    {
        var state = GameState.NewMatch(Seed, Stage, Mode);
        for (var i = 0; i < Inputs.Count; i++)
        {
            Simulation.Step(ref state, Inputs[i]);
            actual = state.Checksum();
            expected = Checksums[i];
            if (actual != expected)
                return i;
        }
        expected = actual = 0;
        return -1;
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
        text.Append("mode ").Append(Mode == MatchMode.Versus ? "versus" : "training").Append('\n');
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

        foreach (var checksum in Checksums)
            text.Append("checksum ").Append(checksum.ToString("x16", CultureInfo.InvariantCulture)).Append('\n');
        text.Append("final ").Append(FinalChecksum.ToString("x16", CultureInfo.InvariantCulture)).Append('\n');
        return text.ToString();
    }

    // Reads the current format. `allowOldVersions` lets rerecord upgrade old files: a version 1 data
    // hash reads as 0, versions 1 and 2 were all played on the training stage, and versions 1 to 3
    // in training mode.
    public static Replay Read(string text, bool allowOldVersions = false)
    {
        using var reader = new StringReader(text);
        var header = Fields(reader.ReadLine(), 2, "header");
        if (header[0] != Magic)
            throw new FormatException("Not a stick-fighter replay");
        var version = int.Parse(header[1], CultureInfo.InvariantCulture);
        var old = version is 1 or 2 or 3;
        if (old && !allowOldVersions)
        {
            var missing = version switch { 1 => "which character data", 2 => "which stage", _ => "which match mode" };
            throw new FormatException($"Replay format {version} doesn't record {missing} it used; rerecord it (ReplayRunner rerecord)");
        }
        if (version != FormatVersion && !old)
            throw new FormatException($"Replay format {header[1]}, but this build reads {FormatVersion}");

        var seed = ulong.Parse(Expect(reader, "seed"), CultureInfo.InvariantCulture);
        var dataHash = version == 1 ? 0 : ulong.Parse(Expect(reader, "data"), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var stage = version <= 2 ? Stage.Training : ReadStage(ExpectFields(reader, "stage", 5));
        var mode = version <= 3 ? MatchMode.Training : ReadMode(Expect(reader, "mode"));
        var frames = int.Parse(Expect(reader, "frames"), CultureInfo.InvariantCulture);
        var inputs = new List<FrameInput>(frames);
        var checksums = new List<ulong>(frames);
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
                case "checksum" when f.Length == 2:
                    checksums.Add(ulong.Parse(f[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                    break;
                case "final" when f.Length == 2:
                    final = ulong.Parse(f[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                    break;
                default:
                    throw new FormatException($"Unexpected replay line: {line}");
            }
        }

        if (inputs.Count != frames || checksums.Count != frames)
            throw new FormatException($"Replay says {frames} frames but has {inputs.Count} inputs and {checksums.Count} checksums");
        var replay = new Replay(seed, dataHash, stage, inputs, checksums, mode);
        if (final != replay.FinalChecksum)
            throw new FormatException("Replay's final checksum doesn't match its last frame");
        return replay;
    }

    private static bool Same(FrameInput a, FrameInput b) => a.Player1 == b.Player1 && a.Player2 == b.Player2;

    private static string Pixels(Numerics.Fix value) => value.FloorToInt().ToString(CultureInfo.InvariantCulture);

    private static MatchMode ReadMode(string mode) => mode switch
    {
        "training" => MatchMode.Training,
        "versus" => MatchMode.Versus,
        _ => throw new FormatException($"Unknown match mode '{mode}' (training or versus)"),
    };

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
