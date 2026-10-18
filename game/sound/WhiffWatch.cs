using System.Collections.Generic;
using StickFighter.Sim;
using StickFighter.Sim.Characters;

namespace StickFighter.Sound;

// Whiffs, worked out in presentation rather than recorded by the sim: a sim event would go into
// GameState and change the checksum of every replay with a missed attack in it. A whiff is a move
// that has reached its first active frame without connecting (one that lands on a later active frame
// still swished first, as a swing does, if an update saw it unconnected).
//
// It follows the ledger's rollback rules as far as one state per update allows. A whiff is keyed by
// its frame, fighter and move, so seeing it again after a resimulation doesn't restart it; one the
// current state contradicts (the fighter isn't in that move any more, while the move would still be
// going) is cancelled, for whiffs inside the rollback window.
public sealed class WhiffWatch
{
    public readonly record struct Whiff(int Frame, int Player, int Move);

    // Longer than any swish lasts.
    private const int Lifetime = 60;

    private readonly List<Whiff> _playing = new();

    public List<Whiff> Started { get; } = new();
    public List<Whiff> Cancelled { get; } = new();

    public void Update(in GameState state)
    {
        Started.Clear();
        Cancelled.Clear();
        var now = state.Frame - 1;
        var moves = Characters.Placeholder.Moves;

        for (var i = _playing.Count - 1; i >= 0; i--)
        {
            var w = _playing[i];
            var move = moves[w.Move];
            var age = now - w.Frame;
            if (age < 0)
                continue; // rolled back to before it: the resimulation will meet it again, or contradict it
            var f = state.Fighters[w.Player];
            var stillIn = f.Action == (int)FighterAction.Attack && f.Move == w.Move && f.ActionFrame >= move.FirstActiveFrame;
            var couldHaveEnded = age >= move.Active + move.Recovery;
            if (age < SimEventRing.Window && !stillIn && !couldHaveEnded)
            {
                Cancelled.Add(w); // a rollback took it away
                _playing.RemoveAt(i);
            }
            else if (age >= Lifetime && age > move.TotalFrames)
            {
                _playing.RemoveAt(i);
            }
        }

        // A move that has reached or passed its first active frame without connecting swishes, once
        // per move instance: an update may come several frames after the first active one (two
        // ticks a display frame at 30 Hz, a catch-up), and the swish still plays, keyed to that frame.
        for (var p = 0; p < GameState.PlayerCount; p++)
        {
            var f = state.Fighters[p];
            if (f.Action != (int)FighterAction.Attack || f.Move < 0 || f.MoveHit)
                continue;
            var move = moves[f.Move];
            if (f.ActionFrame < move.FirstActiveFrame)
                continue;
            var first = now - (f.ActionFrame - move.FirstActiveFrame);
            if (Swished(p, f.Move, first, move.TotalFrames))
                continue;
            var whiff = new Whiff(first, p, f.Move);
            _playing.Add(whiff);
            Started.Add(whiff);
        }
    }

    // Whether this move instance has swished already. Hitstop holds a fighter on one frame of its move
    // while the frames go by, so the first active frame worked out from the move frame can come out
    // later than the swish's: the same instance is any swish of that move since it could have started.
    private bool Swished(int player, int move, int first, int total)
    {
        foreach (var w in _playing)
        {
            if (w.Player == player && w.Move == move && w.Frame <= first && w.Frame > first - total)
                return true;
        }
        return false;
    }

    public void Clear()
    {
        _playing.Clear();
        Started.Clear();
        Cancelled.Clear();
    }
}
