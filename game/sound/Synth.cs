using System;

namespace StickFighter.Sound;

// Every combat sound, synthesised in code: noise bursts, filtered thumps and short pitch sweeps, so
// there are no recordings to license and nothing to ship but this file. Each one is a pure function
// of its name (the noise is a fixed-seed generator), built once at startup. The feel is arcade and
// stylised, like the flat stick look: short, punchy, a clear low end on the heavy things.
//
// No Godot here, so the sounds are tested (length, level, determinism) without an audio device.
public static class Synth
{
    public const int Rate = 22050;

    public static float[] Generate(Sfx sfx) => sfx switch
    {
        Sfx.Whiff => Whiff(),
        Sfx.HitLight => Hit(thump: 260, drop: 0.7f, body: 0.06f, noise: 0.05f, crunch: 1.2f),
        Sfx.HitMedium => Hit(thump: 170, drop: 0.6f, body: 0.09f, noise: 0.08f, crunch: 1.6f),
        Sfx.HitHeavy => Hit(thump: 110, drop: 0.45f, body: 0.14f, noise: 0.12f, crunch: 2.4f),
        Sfx.CounterHit => Counter(),
        Sfx.Block => Block(),
        Sfx.Throw => Throw(),
        Sfx.Knockdown => Knockdown(),
        _ => Ko(),
    };

    // A swish: noise through a band that sweeps up, swelling in and dying away.
    private static float[] Whiff()
    {
        var o = Buffer(0.16f);
        var noise = new Noise(11);
        var band = new Band();
        for (var i = 0; i < o.Length; i++)
        {
            var t = i / (float)Rate;
            var k = t / 0.16f;
            band.Tune(700 + 2600 * k, 0.35f);
            var env = Attack(t, 0.03f) * Decay(t - 0.03f, 0.08f);
            o[i] = band.Run(noise.Next()) * env * 2.2f;
        }
        return Finish(o, 0.7f);
    }

    // A hit: a sine thump dropping in pitch for the weight, a crack of filtered noise on top, and
    // some saturation for punch. Heavier hits are lower, longer and dirtier.
    private static float[] Hit(float thump, float drop, float body, float noise, float crunch)
    {
        var o = Buffer(body + noise + 0.08f);
        var n = new Noise(23);
        var hp = new HighPass(1800);
        var phase = 0.0;
        for (var i = 0; i < o.Length; i++)
        {
            var t = i / (float)Rate;
            var f = thump * (drop + (1 - drop) * MathF.Exp(-t / 0.03f));
            phase += f / Rate;
            var low = MathF.Sin((float)(phase * MathF.Tau)) * Decay(t, body);
            var crack = hp.Run(n.Next()) * Decay(t, noise * 0.5f);
            o[i] = MathF.Tanh((low + crack * 0.8f) * crunch);
        }
        return Finish(o, 0.9f);
    }

    // The counter-hit accent: a heavy hit with a bright two-tone ring over it.
    private static float[] Counter()
    {
        var hit = Hit(thump: 120, drop: 0.5f, body: 0.13f, noise: 0.1f, crunch: 2.2f);
        var o = Buffer(0.32f);
        for (var i = 0; i < o.Length; i++)
        {
            var t = i / (float)Rate;
            var ring = (Square(1320 * t) + Square(1980 * t)) * 0.18f * Decay(t - 0.01f, 0.12f) * Attack(t, 0.01f);
            o[i] = (i < hit.Length ? hit[i] : 0) + ring;
        }
        return Finish(o, 0.9f);
    }

    // A block: a dull, dry clack, quieter and shorter than any hit.
    private static float[] Block()
    {
        var o = Buffer(0.09f);
        var n = new Noise(37);
        var lp = new LowPass(2400);
        for (var i = 0; i < o.Length; i++)
        {
            var t = i / (float)Rate;
            var clack = Square(380 * t) * 0.5f * Decay(t, 0.025f);
            o[i] = clack + lp.Run(n.Next()) * Decay(t, 0.02f);
        }
        return Finish(o, 0.75f);
    }

    // A throw: a low whump with a rush of noise swelling into it.
    private static float[] Throw()
    {
        var o = Buffer(0.3f);
        var n = new Noise(41);
        var lp = new LowPass(900);
        var phase = 0.0;
        for (var i = 0; i < o.Length; i++)
        {
            var t = i / (float)Rate;
            phase += (95 - 45 * Math.Min(1, t / 0.2)) / Rate;
            var whump = MathF.Sin((float)(phase * MathF.Tau)) * Attack(t, 0.05f) * Decay(t - 0.05f, 0.12f);
            var rush = lp.Run(n.Next()) * Attack(t, 0.08f) * Decay(t - 0.08f, 0.06f) * 1.6f;
            o[i] = MathF.Tanh((whump + rush) * 1.8f);
        }
        return Finish(o, 0.85f);
    }

    // A body hitting the floor: a deep thud and a short scuff.
    private static float[] Knockdown()
    {
        var o = Buffer(0.32f);
        var n = new Noise(53);
        var lp = new LowPass(500);
        var phase = 0.0;
        for (var i = 0; i < o.Length; i++)
        {
            var t = i / (float)Rate;
            phase += (70 - 25 * Math.Min(1, t / 0.15)) / Rate;
            var thud = MathF.Sin((float)(phase * MathF.Tau)) * Decay(t, 0.11f);
            var scuff = lp.Run(n.Next()) * Decay(t, 0.05f) * 1.4f;
            o[i] = MathF.Tanh((thud + scuff) * 2f);
        }
        return Finish(o, 0.85f);
    }

    // The KO: the heaviest hit there is, a sub-bass drop under it and a long dark tail.
    private static float[] Ko()
    {
        var hit = Hit(thump: 95, drop: 0.4f, body: 0.18f, noise: 0.14f, crunch: 2.8f);
        var o = Buffer(0.9f);
        var n = new Noise(67);
        var lp = new LowPass(700);
        var phase = 0.0;
        for (var i = 0; i < o.Length; i++)
        {
            var t = i / (float)Rate;
            phase += (120 * MathF.Exp(-t / 0.25f) + 32) / Rate;
            var sub = MathF.Sin((float)(phase * MathF.Tau)) * Attack(t, 0.01f) * Decay(t, 0.35f) * 0.8f;
            var tail = lp.Run(n.Next()) * Decay(t - 0.05f, 0.25f) * 0.7f;
            o[i] = MathF.Tanh((i < hit.Length ? hit[i] : 0) + sub + tail);
        }
        return Finish(o, 0.95f);
    }

    private static float[] Buffer(float seconds) => new float[(int)(seconds * Rate)];

    // Normalised to the given peak, with a few milliseconds' fade at the end so nothing clicks.
    private static float[] Finish(float[] samples, float peak)
    {
        var max = 0f;
        foreach (var s in samples)
            max = MathF.Max(max, MathF.Abs(s));
        var scale = max > 0 ? peak / max : 0;
        var tail = Math.Min(samples.Length, Rate / 200);
        for (var i = 0; i < samples.Length; i++)
        {
            var fade = i >= samples.Length - tail ? (samples.Length - i) / (float)tail : 1;
            samples[i] *= scale * fade;
        }
        return samples;
    }

    private static float Attack(float t, float time) => t <= 0 ? 0 : t >= time ? 1 : t / time;

    private static float Decay(float t, float time) => t <= 0 ? 1 : MathF.Exp(-t / time);

    private static float Square(float cycles) => cycles % 1 < 0.5f ? 1 : -1;

    // White noise from a fixed seed (xorshift), so every run makes the same sound.
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

    private sealed class LowPass
    {
        private readonly float _a;
        private float _y;

        public LowPass(float cutoff) => _a = 1 - MathF.Exp(-MathF.Tau * cutoff / Rate);

        public float Run(float x) => _y += _a * (x - _y);
    }

    private sealed class HighPass
    {
        private readonly LowPass _low;

        public HighPass(float cutoff) => _low = new LowPass(cutoff);

        public float Run(float x) => x - _low.Run(x);
    }

    // A state-variable band-pass, retunable per sample for sweeps.
    private sealed class Band
    {
        private float _f;
        private float _q;
        private float _low;
        private float _band;

        public void Tune(float centre, float damping)
        {
            _f = 2 * MathF.Sin(MathF.PI * centre / Rate);
            _q = damping;
        }

        public float Run(float x)
        {
            _low += _f * _band;
            var high = x - _low - _q * _band;
            _band += _f * high;
            return _band;
        }
    }
}
