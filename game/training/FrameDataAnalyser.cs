using StickFighter.Sim;
using StickFighter.Sim.Characters;

namespace StickFighter.Training;

// What the analyser measured for the last exchange: the attacker's move, and how the hit or block
// left the two of them.
public sealed record FrameReport(
    int Attacker,
    string Move,
    int? Startup,
    int? Active,
    int? Recovery,
    bool Blocked,
    int? Advantage,
    int? Gap, // block strings: frames the defender was free between this block and the last; 0 is a true string
    int Combo, // hits in the combo so far (1 for a single hit)
    int? LinkWindow, // when this hit linked from a recovered attacker: frames it could have started in
    bool MissedFrames, // the display skipped frames, so these numbers aren't trustworthy
    bool Contact = true, // false: the move hasn't touched anyone (yet, or at all: see Whiffed)
    bool Whiffed = false, // the move finished without touching anyone
    string? CancelledFrom = null); // the move this one cancelled or chained out of (so it's no link)

// Training mode's frame data, measured from the simulation rather than looked up: it watches the
// game state after every frame and reports what actually happened, so it catches what the data
// would hide (a hitbox that comes out late, a recovery that runs long, a pushback that breaks a
// link). Presentation only: it reads GameState and writes nothing. Plain C#, so the test can drive
// it frame by frame against the sim's own advantage measurements.
//
// "Free" is the sim's rule: a fighter can act on frame f if, when f began, it had no hitstun,
// blockstun or knockdown left, wasn't held in a throw, and its move (if any) had finished. Advantage
// is when the defender is free minus when the attacker is free, counted from the contact.
public sealed class FrameDataAnalyser
{
    private readonly Character _character = Characters.Placeholder;

    private GameState _previous;
    private bool _hasPrevious;

    private readonly MoveWatch[] _moves = { new(), new() };
    private Exchange? _exchange;
    private int? _lastBlockFree; // the frame the defender came free after its last block
    private int _combo;
    private bool _missed;
    private int _lastStarted = -1; // the player who started the latest move
    private int _clock; // frames that ran, not counting hitstop: what every measurement is in

    public FrameReport? Last { get; private set; }

    // Called with the state after each simulation frame, in order.
    public void Observe(in GameState state)
    {
        var frame = state.Frame - 1; // the frame that just ran
        if (_hasPrevious && frame != _previous.Frame)
        {
            // Skipped (several ticks in one display frame) or rewound (rollback): what's in flight
            // can't be measured honestly.
            _missed = true;
            _exchange = null;
        }

        // Hitstop: on a frozen frame nothing moves, no action frame advances and no stun counts
        // down. Frame data counts only the frames that run (the freeze delays both fighters alike,
        // so it changes no advantage), so frozen frames are passed over.
        if (Sim.Combat.Hitstop.Frozen(state, frame))
        {
            _previous = state;
            _hasPrevious = true;
            return;
        }
        var t = ++_clock; // this frame, counting only frames that ran

        // Who came free on this frame is settled before any new contact replaces the exchange: the
        // defender acting on this frame and then being hit at its end is a gap, not a true string.
        for (var p = 0; p < GameState.PlayerCount; p++)
            WatchMove(p, state, t);
        Resolve(state, t);
        WatchContact(state, t);
        if (_exchange is { } x)
            Report(x);
        else if (_lastStarted >= 0)
            ReportMoveOnly(_lastStarted);

        _previous = state;
        _hasPrevious = true;
    }

    // The attacker's move: when it started, its first and last active frames, when it finished.
    private void WatchMove(int player, in GameState state, int frame)
    {
        var fighter = state.Fighters[player];
        ref var watch = ref _moves[player];
        var attacking = fighter.Action == (int)FighterAction.Attack;
        var started = attacking && fighter.ActionFrame == 0
            && !(_hasPrevious && _previous.Fighters[player].Action == (int)FighterAction.Attack
                 && _previous.Fighters[player].Move == fighter.Move && _previous.Fighters[player].ActionFrame == 0);
        if (started)
        {
            var previous = watch;
            watch = new MoveWatch { Move = fighter.Move, Start = frame };
            _missed = false; // a fresh move, seen from its first frame
            _lastStarted = player;

            // A finished exchange belongs to the last move; this one starts its own. (One still in
            // play stays, so a combo or a block string can follow on from it.)
            if (_exchange is { AttackerFree: not null, DefenderFree: not null })
                _exchange = null;

            // A new move before the old one finished is a cancel. One starting on the very frame the
            // old one ended (a buffered press) followed a recovered attacker: a link.
            var finished = previous.Move < 0 || previous.End is not null
                || (_hasPrevious && _previous.Fighters[player].ActionFrame + 1 >= _character.Moves[previous.Move].TotalFrames);
            watch.CancelledFrom = finished ? -1 : previous.Move;
        }
        if (watch.Move < 0)
            return;
        if (attacking && fighter.Move == watch.Move && watch.End is null)
        {
            if (_character.Moves[watch.Move].IsActive(fighter.ActionFrame))
            {
                watch.FirstActive ??= frame;
                watch.LastActive = frame;
            }
        }
        else if (watch.End is null)
        {
            watch.End = frame; // the first frame it could act again
        }
    }

    // A hit or a block landing: hitstun or blockstun freshly applied (more than last frame's, less one).
    private void WatchContact(in GameState state, int frame)
    {
        for (var defender = 0; defender < GameState.PlayerCount; defender++)
        {
            var now = state.Fighters[defender];
            var before = _hasPrevious ? _previous.Fighters[defender] : default;
            var hit = now.Hitstun > System.Math.Max(before.Hitstun - 1, 0)
                || (now.KnockedDown && !before.KnockedDown && before.Hitstun == 0);
            var blocked = now.Blockstun > System.Math.Max(before.Blockstun - 1, 0);
            if (!hit && !blocked)
                continue;

            var attacker = 1 - defender;
            var stillStunned = _exchange is { DefenderFree: null } open && open.Defender == defender;
            // The gap since the last block: none if the defender never came free (a true string),
            // otherwise the frames it was free for, up to and including this one (it acted on them).
            int? gap = null;
            if (blocked)
                gap = stillStunned ? 0 : _lastBlockFree is { } free ? System.Math.Max(0, frame - free + 1) : null;
            _combo = hit && stillStunned && !(_exchange?.Blocked ?? true) ? _combo + 1 : hit ? 1 : 0;

            // A link: the attacker had recovered from its last move before starting this one, and the
            // defender was still in hitstun from it.
            int? link = null;
            if (hit && _combo > 1 && _exchange is { } last && last.AttackerFree is { } recovered && _moves[attacker].CancelledFrom < 0)
            {
                var startup = _character.Moves[_moves[attacker].Move].Startup;
                link = last.DefenderStunLeftAt(recovered) - startup + 1;
            }

            _exchange = new Exchange
            {
                Attacker = attacker,
                Defender = defender,
                Contact = frame,
                Blocked = blocked && !hit,
                Gap = blocked ? gap : null,
                Link = link,
            };
            _exchange.RecordStun(frame, blocked && !hit ? now.Blockstun : now.Hitstun);
        }
    }

    // Who comes free when, after the contact, and the report once both are.
    private void Resolve(in GameState state, int frame)
    {
        if (_exchange is not { } x)
            return;
        // The attacker is free on the first frame it's not attacking, or on which it starts a new move
        // after the last one finished (a buffered press comes out on the very first free frame).
        var attacker = state.Fighters[x.Attacker];
        var watch = _moves[x.Attacker];
        var restarted = watch.Start == frame && watch.Start > x.Contact && watch.CancelledFrom < 0;
        if (x.AttackerFree is null && frame >= x.Contact && (attacker.Action != (int)FighterAction.Attack || restarted))
            x.AttackerFree = frame;
        if (x.DefenderFree is null && frame > x.Contact && _hasPrevious && Free(_previous.Fighters[x.Defender]))
        {
            x.DefenderFree = frame;
            if (x.Blocked)
                _lastBlockFree = frame;
        }
    }

    // Free when a frame begins, by the sim's rule (the state passed is the one before the frame).
    private bool Free(in FighterState f) =>
        f.Hitstun == 0 && f.Blockstun == 0 && !f.KnockedDown && f.Grab < 0
        && !(f.Action == (int)FighterAction.Attack && f.ActionFrame + 1 < _character.Moves[f.Move].TotalFrames);

    private void Report(Exchange x)
    {
        var watch = _moves[x.Attacker];
        var move = watch.Move >= 0 ? _character.Moves[watch.Move] : null;
        Last = new FrameReport(
            x.Attacker,
            move?.Input ?? "?",
            watch.FirstActive - watch.Start + 1,
            watch.LastActive - watch.FirstActive + 1,
            watch.End - watch.LastActive - 1,
            x.Blocked,
            x.DefenderFree - x.AttackerFree,
            x.Gap,
            x.Blocked ? 0 : _combo,
            x.Link,
            _missed,
            CancelledFrom: CancelledFrom(watch));
    }

    private string? CancelledFrom(in MoveWatch watch) =>
        watch.CancelledFrom >= 0 ? _character.Moves[watch.CancelledFrom].Input : null;

    // A move that hasn't touched anyone: its own frames, and once it's over, a whiff.
    private void ReportMoveOnly(int player)
    {
        var watch = _moves[player];
        Last = new FrameReport(
            player,
            _character.Moves[watch.Move].Input,
            watch.FirstActive - watch.Start + 1,
            watch.LastActive - watch.FirstActive + 1,
            watch.End - watch.LastActive - 1,
            Blocked: false,
            Advantage: null,
            Gap: null,
            Combo: 0,
            LinkWindow: null,
            _missed,
            Contact: false,
            Whiffed: watch.End is not null,
            CancelledFrom: CancelledFrom(watch));
    }

    private struct MoveWatch
    {
        public MoveWatch()
        {
        }

        public int Move = -1;
        public int Start;
        public int? FirstActive;
        public int? LastActive;
        public int? End;
        public int CancelledFrom = -1;
    }

    private sealed class Exchange
    {
        public int Attacker;
        public int Defender;
        public int Contact;
        public bool Blocked;
        public int? Gap;
        public int? Link;
        public int? AttackerFree;
        public int? DefenderFree;

        private int _stunFrame;
        private int _stun;

        public void RecordStun(int frame, int stun) => (_stunFrame, _stun) = (frame, stun);

        // Stun the defender has left when frame `at` begins (one point is spent per frame).
        public int DefenderStunLeftAt(int at) => System.Math.Max(0, _stun - (at - 1 - _stunFrame));
    }
}
