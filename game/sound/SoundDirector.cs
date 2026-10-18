using System;
using System.Collections.Generic;
using StickFighter.FX;
using StickFighter.Sim;

namespace StickFighter.Sound;

// Where sounds go: a voice plays one sound at a pitch and level, or fades out what it's playing.
// The game's is Godot's audio (SoundView); tests and scenarios use fakes, since CI has no audio device.
public interface IAudioSink
{
    void Play(int voice, Sfx sfx, float pitch, float gainDb);

    void Fade(int voice);
}

// Plays the combat sounds from the sim's events, rollback-safe through the same ledger as the hit
// effects: a sound starts once per (frame, kind, attacker, move) however often its frame is
// simulated, and one whose cause a rollback removed fades out (a short fade, not a click). Whiffs
// come from WhiffWatch, under the same rules.
//
// A few voices only: when they're all busy, the oldest sound gives way, so a flurry of hits never
// piles up. Each sound's pitch varies slightly with its event, deterministically, so repeats don't
// sound mechanical and a replay sounds the same every time. The level follows the effects volume
// from Options.
public sealed class SoundDirector
{
    public const int Voices = 6;

    // Below this the volume is silence (0 in Options).
    public const float SilentDb = -80;

    private readonly IAudioSink _sink;
    private readonly EffectLedger _ledger = new();
    private readonly WhiffWatch _whiffs = new();
    private readonly Cue?[] _voices = new Cue?[Voices];
    private long _started;

    public SoundDirector(IAudioSink sink) => _sink = sink;

    // Options' effects volume, 0 to 100.
    public int EffectsVolume { get; set; } = 80;

    // When set, a line per sound started or cancelled: "frame 7  HitHeavy  P1  pitch 1.02".
    public List<string>? Log { get; set; }

    // A sound playing, by what caused it.
    private readonly record struct Cue(int Frame, Sfx Sfx, int Attacker, int Move, long Order);

    public void Update(in GameState state)
    {
        _ledger.Update(state);
        _whiffs.Update(state);

        // A voice is free again once its sound is over (none lasts a second).
        for (var v = 0; v < Voices; v++)
        {
            if (_voices[v] is { } c && _ledger.Now - c.Frame > EffectLedger.Lifetime)
                _voices[v] = null;
        }

        foreach (var e in _ledger.Cancelled)
        {
            if (Cues.For(e) is { } sfx)
                Stop(e.Frame, sfx, e.Attacker, e.Move);
        }
        foreach (var w in _whiffs.Cancelled)
            Stop(w.Frame, Sfx.Whiff, w.Player, w.Move);

        foreach (var e in _ledger.Started)
        {
            if (Cues.For(e) is { } sfx)
                Start(e.Frame, sfx, e.Attacker, e.Move);
        }
        foreach (var w in _whiffs.Started)
            Start(w.Frame, Sfx.Whiff, w.Player, w.Move);
    }

    // A new match or a scenario reset: everything stops.
    public void Clear()
    {
        for (var v = 0; v < Voices; v++)
        {
            if (_voices[v] is not null)
                _sink.Fade(v);
            _voices[v] = null;
        }
        _ledger.Clear();
        _whiffs.Clear();
    }

    // The effects volume as a level: a curve that feels even across the slider, silent at 0.
    public static float VolumeDb(int volume) =>
        volume <= 0 ? SilentDb : MathF.Max(SilentDb, 40 * MathF.Log10(Math.Min(volume, 100) / 100f));

    // A pitch within ±5% from the event's identity: the same event always sounds the same.
    public static float PitchFor(int frame, Sfx sfx, int attacker, int move)
    {
        var h = (uint)(frame * 73856093) ^ (uint)((int)sfx * 19349663) ^ (uint)(attacker * 83492791) ^ (uint)((move + 1) * 2654435761u);
        h ^= h >> 15;
        h *= 2246822519u;
        h ^= h >> 13;
        return 0.95f + h % 1001 / 10000f;
    }

    private void Start(int frame, Sfx sfx, int attacker, int move)
    {
        var voice = FreeVoice();
        var pitch = PitchFor(frame, sfx, attacker, move);
        _voices[voice] = new Cue(frame, sfx, attacker, move, _started++);
        _sink.Play(voice, sfx, pitch, VolumeDb(EffectsVolume) + Cues.LevelDb(sfx));
        Log?.Add($"frame {frame,4}  {sfx,-10}  P{attacker + 1}  pitch {pitch:0.000}");
    }

    private void Stop(int frame, Sfx sfx, int attacker, int move)
    {
        for (var v = 0; v < Voices; v++)
        {
            if (_voices[v] is { } c && c.Frame == frame && c.Sfx == sfx && c.Attacker == attacker && c.Move == move)
            {
                _sink.Fade(v);
                _voices[v] = null;
            }
        }
        Log?.Add($"frame {frame,4}  {sfx,-10}  P{attacker + 1}  cancelled");
    }

    // A free voice, or the one that started longest ago (it gives way).
    private int FreeVoice()
    {
        var oldest = 0;
        for (var v = 0; v < Voices; v++)
        {
            if (_voices[v] is null)
                return v;
            if (_voices[v]!.Value.Order < _voices[oldest]!.Value.Order)
                oldest = v;
        }
        _sink.Fade(oldest);
        return oldest;
    }
}
