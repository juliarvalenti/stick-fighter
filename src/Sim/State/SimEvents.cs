using System.Runtime.CompilerServices;
using StickFighter.Sim.Numerics;

namespace StickFighter.Sim;

// What happened on a frame that the presentation shows: sparks, dust, a KO flash, screen shake.
public enum EventKind
{
    None = 0,
    Hit = 1,
    CounterHit = 2,
    Block = 3,
    Throw = 4,
    Knockdown = 5,
    Ko = 6,
}

// One event: when, what, who did it to whom with which move (-1 for none), and where (the contact
// point, or the defender's feet).
public struct SimEvent
{
    public const int EncodedSize = 4 + 4 + 4 + 4 + 4 + 16;

    public int Frame;
    public EventKind Kind;
    public int Attacker;
    public int Defender;
    public int Move;
    public FixVec2 Point;

    public readonly void Encode(ref StateWriter writer)
    {
        writer.Write(Frame);
        writer.Write((int)Kind);
        writer.Write(Attacker);
        writer.Write(Defender);
        writer.Write(Move);
        writer.Write(Point);
    }
}

// The events of the last few frames, in GameState, so they save, load and roll back with the match:
// after a rollback and resimulation, the ring says what really happened on each of those frames.
// The presentation reconciles its effects against it (game/fx/EffectLedger), so an effect plays once
// however often its frame is simulated, and one whose cause a rollback removed is cancelled.
//
// Window frames back is enough for the rollback limit and for the most ticks one display frame runs.
// The ring holds Window frames' worth of the most events a frame can produce, so nothing in the
// window is ever overwritten.
[InlineArray(Capacity)]
public struct SimEventRing
{
    // How many frames back the ring is complete for.
    public const int Window = 8;

    // The most events one frame can record. Each fighter's contact gives a hit (or counter hit, or
    // block), and may add a knockdown (a sweep) and a KO: 3 each, 6 for a trade. Each fighter can
    // also land from a juggle that frame, a knockdown each: 2 more. A completed throw gives 3 (throw,
    // knockdown, KO), but grabbed fighters have no hitboxes, so it can't coincide with the contacts.
    public const int MaxPerFrame = 8;

    public const int Capacity = Window * MaxPerFrame;

    public const int EncodedSize = Capacity * SimEvent.EncodedSize;

    private SimEvent _element;
}

public static class SimEvents
{
    // Records an event on the current frame, overwriting the oldest one.
    public static void Add(ref GameState state, EventKind kind, int attacker, int defender, int move, FixVec2 point)
    {
        state.Events[state.NextEvent] = new SimEvent
        {
            Frame = state.Frame,
            Kind = kind,
            Attacker = attacker,
            Defender = defender,
            Move = move,
            Point = point,
        };
        state.NextEvent = (state.NextEvent + 1) % SimEventRing.Capacity;
    }
}
