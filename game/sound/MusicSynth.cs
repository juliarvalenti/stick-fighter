using System;
using System.Collections.Generic;

namespace StickFighter.Sound;

public enum Track
{
    Menu,
    Match,
}

// The music, synthesised in code like the combat sounds: two short loops in A minor, built once at
// startup, the same every run. The menu loop is slow and airy (a pad, a bell melody, a soft beat);
// the match loop is driving arcade (kick and snare, an eighth-note bass, a pulse arpeggio in its
// second half). About 33 seconds of material in all.
//
// Each loop is seamless: a note's tail that runs past the end wraps round to the start, so the
// last sample flows into the first.
public static class MusicSynth
{
    public const int Rate = Synth.Rate;

    // Chords a bar each: Am F C G | Am F G E.
    private static readonly int[][] Chords =
    {
        new[] { 57, 60, 64 }, new[] { 53, 57, 60 }, new[] { 60, 64, 67 }, new[] { 55, 59, 62 },
        new[] { 57, 60, 64 }, new[] { 53, 57, 60 }, new[] { 55, 59, 62 }, new[] { 52, 56, 59 },
    };

    private static readonly int[] Bass = { 45, 41, 48, 43, 45, 41, 43, 40 };

    public static float[] Generate(Track track) => track == Track.Menu ? Menu() : Match();

    public static float Seconds(Track track) => Bars * 4 * 60f / Bpm(track);

    private const int Bars = 8;

    private static float Bpm(Track track) => track == Track.Menu ? 100 : 140;

    private static float[] Match()
    {
        var beat = 60f / Bpm(Track.Match);
        var o = new float[(int)(Seconds(Track.Match) * Rate)];
        var noise = new Noise(5);
        for (var bar = 0; bar < Bars; bar++)
        {
            var t0 = bar * 4 * beat;
            foreach (var b in new[] { 0f, 2f, 2.5f })
                Add(o, t0 + b * beat, Kick(0.9f));
            foreach (var b in new[] { 1f, 3f })
                Add(o, t0 + b * beat, Snare(noise, 0.55f));
            for (var e = 0; e < 8; e++)
                Add(o, t0 + e * beat / 2, Hat(noise, e % 2 == 0 ? 0.16f : 0.1f));

            // The bass: root eighths, up the octave on the off-beats.
            for (var e = 0; e < 8; e++)
                Add(o, t0 + e * beat / 2, Saw(Hz(Bass[bar] + (e % 2 == 1 ? 12 : 0)), beat / 2 * 0.9f, 0.32f, cutoff: 700));

            // The second half: a sixteenth arpeggio over the chord, an octave up.
            if (bar >= 4)
            {
                var chord = Chords[bar];
                for (var s = 0; s < 16; s++)
                {
                    var note = chord[s % 3] + 12 + (s % 6 >= 3 ? 12 : 0);
                    Add(o, t0 + s * beat / 4, Pulse(Hz(note), beat / 4 * 0.8f, 0.09f));
                }
            }
        }
        return Normalise(o, 0.8f);
    }

    private static float[] Menu()
    {
        var beat = 60f / Bpm(Track.Menu);
        var o = new float[(int)(Seconds(Track.Menu) * Rate)];
        var noise = new Noise(9);
        // A bell melody, (beat in the loop, note, beats long).
        var melody = new List<(float, int, float)>
        {
            (0, 76, 1.5f), (1.5f, 72, 0.5f), (2, 71, 2),
            (4, 72, 1.5f), (5.5f, 69, 0.5f), (6, 72, 2),
            (8, 72, 1.5f), (9.5f, 67, 0.5f), (10, 64, 2),
            (12, 71, 1), (13, 74, 1), (14, 71, 2),
            (16, 76, 1.5f), (17.5f, 72, 0.5f), (18, 71, 2),
            (20, 72, 1.5f), (21.5f, 69, 0.5f), (22, 77, 2),
            (24, 74, 1), (25, 71, 1), (26, 67, 2),
            (28, 68, 2), (30, 71, 2),
        };

        for (var bar = 0; bar < Bars; bar++)
        {
            var t0 = bar * 4 * beat;
            foreach (var note in Chords[bar])
                Add(o, t0, Pad(Hz(note), 4 * beat, 0.13f));
            Add(o, t0, Saw(Hz(Bass[bar]), 4 * beat * 0.95f, 0.22f, cutoff: 400));
            Add(o, t0, Kick(0.45f));
            Add(o, t0 + 2.5f * beat, Kick(0.3f));
            Add(o, t0 + 2 * beat, Snare(noise, 0.18f));
            for (var e = 0; e < 4; e++)
                Add(o, t0 + (e + 0.5f) * beat, Hat(noise, 0.07f));
        }
        foreach (var (at, note, beats) in melody)
            Add(o, at * beat, Bell(Hz(note), beats * beat, 0.28f));
        return Normalise(o, 0.75f);
    }

    // Adds a sound at `seconds`, wrapping any tail past the end round to the start.
    private static void Add(float[] o, float seconds, float[] sound)
    {
        var start = (int)(seconds * Rate);
        for (var i = 0; i < sound.Length; i++)
            o[(start + i) % o.Length] += sound[i];
    }

    private static float Hz(int midi) => 440f * MathF.Pow(2, (midi - 69) / 12f);

    private static float[] Kick(float level)
    {
        var o = new float[(int)(0.25f * Rate)];
        var phase = 0.0;
        for (var i = 0; i < o.Length; i++)
        {
            var t = i / (float)Rate;
            phase += (45 + 110 * MathF.Exp(-t / 0.03f)) / Rate;
            o[i] = MathF.Tanh(MathF.Sin((float)(phase * MathF.Tau)) * MathF.Exp(-t / 0.12f) * 1.6f) * level;
        }
        return o;
    }

    private static float[] Snare(Noise noise, float level)
    {
        var o = new float[(int)(0.18f * Rate)];
        var low = 0f;
        for (var i = 0; i < o.Length; i++)
        {
            var t = i / (float)Rate;
            var n = noise.Next();
            low += 0.3f * (n - low);
            var body = MathF.Sin(MathF.Tau * 190 * t) * MathF.Exp(-t / 0.04f);
            o[i] = ((n - low) * MathF.Exp(-t / 0.06f) + body * 0.6f) * level;
        }
        return o;
    }

    private static float[] Hat(Noise noise, float level)
    {
        var o = new float[(int)(0.05f * Rate)];
        var low = 0f;
        for (var i = 0; i < o.Length; i++)
        {
            var t = i / (float)Rate;
            var n = noise.Next();
            low += 0.6f * (n - low);
            o[i] = (n - low) * MathF.Exp(-t / 0.012f) * level;
        }
        return o;
    }

    // A sawtooth through a low-pass, plucked: quick attack, settling, released at the end.
    private static float[] Saw(float hz, float seconds, float level, float cutoff)
    {
        var o = new float[(int)((seconds + 0.03f) * Rate)];
        var a = 1 - MathF.Exp(-MathF.Tau * cutoff / Rate);
        var y = 0f;
        for (var i = 0; i < o.Length; i++)
        {
            var t = i / (float)Rate;
            var x = 2 * (hz * t % 1) - 1;
            y += a * (x - y);
            o[i] = y * Envelope(t, seconds, 0.005f, 0.6f) * level;
        }
        return o;
    }

    private static float[] Pulse(float hz, float seconds, float level)
    {
        var o = new float[(int)((seconds + 0.02f) * Rate)];
        for (var i = 0; i < o.Length; i++)
        {
            var t = i / (float)Rate;
            o[i] = (hz * t % 1 < 0.25f ? 1 : -1) * Envelope(t, seconds, 0.003f, 0.5f) * level;
        }
        return o;
    }

    // Two detuned saws, softened and slow to swell: the menu's chords.
    private static float[] Pad(float hz, float seconds, float level)
    {
        var o = new float[(int)((seconds + 0.4f) * Rate)];
        var a = 1 - MathF.Exp(-MathF.Tau * 900 / Rate);
        var y = 0f;
        for (var i = 0; i < o.Length; i++)
        {
            var t = i / (float)Rate;
            var x = (2 * (hz * 1.004f * t % 1) - 1) + (2 * (hz * 0.996f * t % 1) - 1);
            y += a * (x / 2 - y);
            var swell = MathF.Min(1, t / 0.5f) * (t > seconds ? MathF.Exp(-(t - seconds) / 0.12f) : 1);
            o[i] = y * swell * level;
        }
        return o;
    }

    // A sine with an inharmonic partial, struck and ringing out.
    private static float[] Bell(float hz, float seconds, float level)
    {
        var o = new float[(int)((seconds + 0.6f) * Rate)];
        for (var i = 0; i < o.Length; i++)
        {
            var t = i / (float)Rate;
            var tone = MathF.Sin(MathF.Tau * hz * t) + 0.3f * MathF.Sin(MathF.Tau * hz * 2.76f * t) * MathF.Exp(-t / 0.15f);
            o[i] = tone * MathF.Min(1, t / 0.004f) * MathF.Exp(-t / (0.35f + seconds * 0.3f)) * level;
        }
        return o;
    }

    // Attack, a fall to `sustain`, and a 20 ms release after `seconds`.
    private static float Envelope(float t, float seconds, float attack, float sustain)
    {
        if (t < attack)
            return t / attack;
        if (t > seconds)
            return MathF.Max(0, sustain * (1 - (t - seconds) / 0.02f));
        return sustain + (1 - sustain) * MathF.Exp(-(t - attack) / 0.06f);
    }

    private static float[] Normalise(float[] o, float peak)
    {
        var max = 0f;
        foreach (var s in o)
            max = MathF.Max(max, MathF.Abs(s));
        for (var i = 0; i < o.Length; i++)
            o[i] = max > 0 ? o[i] * peak / max : 0;
        return o;
    }

    private sealed class Noise
    {
        private uint _state;

        public Noise(uint seed) => _state = seed * 2654435761u + 1;

        public float Next()
        {
            _state ^= _state << 13;
            _state ^= _state >> 17;
            _state ^= _state << 5;
            return _state / (float)uint.MaxValue * 2 - 1;
        }
    }
}
