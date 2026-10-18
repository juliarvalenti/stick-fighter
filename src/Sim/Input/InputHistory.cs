using System;
using System.Runtime.CompilerServices;

namespace StickFighter.Sim;

// The last Depth frames of one player's cleaned input, indexed by frame number.
// It lives in GameState, so it saves, loads and checksums with the match: links and
// motions read it, so a rollback has to restore it.
// Press and release edges are derived from neighbouring frames, which covers negative edge.
[InlineArray(Depth)]
public struct InputHistory
{
    // Enough for the buffer window to reach back across the longest hitstop (Combat.Hitstop).
    public const int Depth = 32;

    // The longest window a query may ask about: each frame needs the one before it for edges.
    public const int MaxWindow = Depth - 1;

    private Buttons _element;

    public void Record(int frame, Buttons buttons) => this[Slot(frame)] = buttons;

    // Input on this frame, or None before the match started or once it has left the buffer.
    public readonly Buttons At(int frame, int now) =>
        frame < 0 || frame > now || now - frame >= Depth ? Buttons.None : this[Slot(frame)];

    public readonly Buttons PressedOn(int frame, int now) => At(frame, now) & ~At(frame - 1, now);

    public readonly Buttons ReleasedOn(int frame, int now) => At(frame - 1, now) & ~At(frame, now);

    // True if any of these buttons went down in the last `window` frames, counting `now`.
    public readonly bool Pressed(Buttons buttons, int now, int window)
    {
        CheckWindow(window);
        for (var frame = now; frame > now - window; frame--)
        {
            if (PressedOn(frame, now).HasAny(buttons))
                return true;
        }
        return false;
    }

    // True if any of these buttons came up in the last `window` frames, counting `now` (negative edge).
    public readonly bool Released(Buttons buttons, int now, int window)
    {
        CheckWindow(window);
        for (var frame = now; frame > now - window; frame--)
        {
            if (ReleasedOn(frame, now).HasAny(buttons))
                return true;
        }
        return false;
    }

    public readonly void Encode(ref StateWriter writer)
    {
        for (var i = 0; i < Depth; i++)
            writer.Write((int)this[i]);
    }

    public const int EncodedSize = Depth * 4;

    private static int Slot(int frame) => frame % Depth;

    private static void CheckWindow(int window)
    {
        if (window < 1 || window > MaxWindow)
            throw new ArgumentOutOfRangeException(nameof(window), $"Window must be 1 to {MaxWindow} frames");
    }
}

[InlineArray(GameState.PlayerCount)]
public struct InputHistories
{
    private InputHistory _element;
}
