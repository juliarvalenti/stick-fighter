using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace StickFighter.Sim.Replays;

// A recorded match: the seed, both players' inputs for every frame, and the checksum after
// every frame. Replaying the inputs from the seed must reproduce every checksum; that's the
// determinism gate.
//
// Text format, one item per line so diffs read well:
//
//   stick-fighter-replay 1
//   seed 42
//   frames 3
//   input 0008 0004 2        p1 buttons, p2 buttons (hex), run length
//   input 0000 0000 1
//   checksum 9f86d081884c7d65 (one per frame, after that frame)
//   ...
//   final 9f86d081884c7d65
public sealed class Replay
{
    public const string Magic = "stick-fighter-replay";
    public const int FormatVersion = 1;

    public ulong Seed { get; }
    public IReadOnlyList<FrameInput> Inputs { get; }
    public IReadOnlyList<ulong> Checksums { get; }

    public Replay(ulong seed, IReadOnlyList<FrameInput> inputs, IReadOnlyList<ulong> checksums)
    {
        if (inputs.Count != checksums.Count)
            throw new ArgumentException("A replay needs one checksum per frame of input");
        Seed = seed;
        Inputs = inputs;
        Checksums = checksums;
    }

    public int Frames => Inputs.Count;

    public ulong FinalChecksum => Checksums.Count > 0 ? Checksums[^1] : GameState.NewMatch(Seed).Checksum();

    // Plays the inputs from the seed and records the checksums: how a replay is made from inputs alone.
    public static Replay Record(ulong seed, IReadOnlyList<FrameInput> inputs)
    {
        var state = GameState.NewMatch(seed);
        var checksums = new ulong[inputs.Count];
        for (var i = 0; i < inputs.Count; i++)
        {
            Simulation.Step(ref state, inputs[i]);
            checksums[i] = state.Checksum();
        }
        return new Replay(seed, inputs, checksums);
    }

    // Replays the inputs and returns the first frame whose checksum differs, or -1 if all match.
    public int FirstDivergence(out ulong expected, out ulong actual)
    {
        var state = GameState.NewMatch(Seed);
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

    public static Replay Read(string text)
    {
        using var reader = new StringReader(text);
        var header = Fields(reader.ReadLine(), 2, "header");
        if (header[0] != Magic)
            throw new FormatException("Not a stick-fighter replay");
        if (int.Parse(header[1], CultureInfo.InvariantCulture) != FormatVersion)
            throw new FormatException($"Replay format {header[1]}, but this build reads {FormatVersion}");

        var seed = ulong.Parse(Expect(reader, "seed"), CultureInfo.InvariantCulture);
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
        var replay = new Replay(seed, inputs, checksums);
        if (final != replay.FinalChecksum)
            throw new FormatException("Replay's final checksum doesn't match its last frame");
        return replay;
    }

    private static bool Same(FrameInput a, FrameInput b) => a.Player1 == b.Player1 && a.Player2 == b.Player2;

    private static string[] Fields(string? line, int count, string what)
    {
        var fields = line?.Split(' ');
        if (fields is null || fields.Length != count)
            throw new FormatException($"Bad replay {what}: {line}");
        return fields;
    }

    private static string Expect(StringReader reader, string key)
    {
        var fields = Fields(reader.ReadLine(), 2, key);
        if (fields[0] != key)
            throw new FormatException($"Expected '{key}', found '{fields[0]}'");
        return fields[1];
    }
}
