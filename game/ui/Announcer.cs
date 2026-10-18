using System.Collections.Generic;
using StickFighter.Sim;

namespace StickFighter.UI;

// Each announcement starts once: the hook for the announcer's voice (Sound) and anything else
// that must happen once per call, however many times rollback presents the frames it's on. The
// picture needs none of this (it's drawn from the state); a one-off does.
//
// A call is keyed by its round and kind, its identity in the match, rather than by the frame it
// started on: a corrected rollback can move a K.O. by a frame, and it's still the one K.O.
public sealed class Announcer
{
    private readonly HashSet<(int Round, Announcement Kind)> _started = new();
    private readonly List<(int Round, Announcement Kind)> _log = new();

    // Every call started so far, in order.
    public IReadOnlyList<(int Round, Announcement Kind)> Started => _log;

    // The call showing on this frame, if it hasn't started before; null otherwise.
    public (int Round, Announcement Kind)? Present(in GameState state)
    {
        if (MatchHud.Announcement(state) is not { } showing)
            return null;
        var key = (showing.Round, showing.Kind);
        if (!_started.Add(key))
            return null;
        _log.Add(key);
        return key;
    }
}
