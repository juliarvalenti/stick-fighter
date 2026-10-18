using System.Collections.Generic;
using StickFighter.Sim;

namespace StickFighter.FX;

// Which effects are playing, kept rollback-safe. The sim records what happened in a ring of recent
// events in GameState (SimEvents); after each update the ledger reconciles against it, for the
// frames the ring covers:
//  - an event it hasn't seen starts its effect (once: a resimulated frame finds it already playing);
//  - an event it's playing that the ring no longer has (a rollback took its cause away) is cancelled.
// An effect is keyed by its frame and cause (kind, attacker, move), so the same hit resimulated is
// the same effect. Frames past the ring are settled and left alone.
//
// No Godot here, so the rollback rules are tested without a scene (tests/Presentation.Tests).
public sealed class EffectLedger
{
    public readonly record struct Key(int Frame, EventKind Kind, int Attacker, int Move);

    public static Key KeyOf(in SimEvent e) => new(e.Frame, e.Kind, e.Attacker, e.Move);

    // Effects started this long ago are dropped: longer than any effect or shake lasts.
    public const int Lifetime = 60;

    private readonly List<SimEvent> _playing = new();

    // What's playing, oldest first; its frame in the effect is the last simulated frame minus its Frame.
    public IReadOnlyList<SimEvent> Playing => _playing;

    // What the last Update started and cancelled.
    public List<SimEvent> Started { get; } = new();
    public List<SimEvent> Cancelled { get; } = new();

    // The last simulated frame, as of the last Update.
    public int Now { get; private set; } = -1;

    public void Update(in GameState state)
    {
        Started.Clear();
        Cancelled.Clear();
        Now = state.Frame - 1;
        var from = Now - SimEventRing.Window + 1;

        for (var i = _playing.Count - 1; i >= 0; i--)
        {
            var e = _playing[i];
            if (e.Frame >= from && e.Frame <= Now && !InRing(state, KeyOf(e)))
            {
                Cancelled.Add(e);
                _playing.RemoveAt(i);
            }
            else if (e.Frame < Now - Lifetime)
            {
                _playing.RemoveAt(i);
            }
        }

        for (var slot = 0; slot < SimEventRing.Capacity; slot++)
        {
            var e = state.Events[slot];
            if (e.Kind == EventKind.None || e.Frame < from || e.Frame > Now || IsPlaying(KeyOf(e)))
                continue;
            _playing.Add(e);
            Started.Add(e);
        }
    }

    // Forget everything (a new match, or a scenario reset).
    public void Clear()
    {
        _playing.Clear();
        Started.Clear();
        Cancelled.Clear();
    }

    private bool IsPlaying(Key key)
    {
        foreach (var e in _playing)
        {
            if (KeyOf(e) == key)
                return true;
        }
        return false;
    }

    private static bool InRing(in GameState state, Key key)
    {
        for (var slot = 0; slot < SimEventRing.Capacity; slot++)
        {
            var e = state.Events[slot];
            if (e.Kind != EventKind.None && KeyOf(e) == key)
                return true;
        }
        return false;
    }

    // Screen shake for an event, (amplitude in pixels, frames): heavy hits and counters, throws, and
    // most of all KOs. None for the rest.
    public static (float Amplitude, int Frames) ShakeFor(in SimEvent e)
    {
        var heavy = e.Move >= 0 && Sim.Characters.Characters.Placeholder.Moves[e.Move].Strength == Sim.Characters.Strength.Heavy;
        return e.Kind switch
        {
            EventKind.Ko => (14f, 30),
            EventKind.Throw => (6f, 12),
            EventKind.CounterHit => (heavy ? 9f : 5f, 12),
            EventKind.Hit when heavy => (6f, 10),
            _ => (0f, 0),
        };
    }
}
