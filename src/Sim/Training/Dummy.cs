using StickFighter.Sim.Characters;

namespace StickFighter.Sim.Training;

public enum DummyAction
{
    Stand,
    Crouch,

    // Neutral jumps, one after another.
    Jump,

    // Guards everything: down-back against a low, back against anything else.
    BlockAll,

    // Lets the first hit through, then guards until it's been left alone for a second.
    BlockAfterFirstHit,

    // Plays an InputRecording back, once or on a loop.
    Playback,
}

// The training dummy: a player's buttons, from what it can see in the state, as a player would.
// It never touches the state, only presses buttons, so a match against it records, replays and rolls
// back like any other. It keeps a little memory (when it was last hit, where it is in a playback),
// so give it the frames in order; a replay of the match needs only the buttons it pressed.
public sealed class Dummy
{
    // Block after first hit: frames it keeps guarding after its last hitstun or blockstun.
    public const int GuardHoldFrames = 60;

    private DummyAction _action;
    private int _guardUntil = -1;
    private int _playbackFrame;

    public DummyAction Action
    {
        get => _action;
        set
        {
            _action = value;
            Restart();
        }
    }

    // Starts the setting over: no hit remembered, a playback from the top.
    public void Restart()
    {
        _guardUntil = -1;
        _playbackFrame = 0;
    }

    public InputRecording Recording { get; set; } = new();

    public bool Loop { get; set; } = true;

    // This frame's buttons for `player`, from the state before the frame.
    public Buttons Next(in GameState state, int player)
    {
        switch (_action)
        {
            case DummyAction.Crouch:
                return Buttons.Down;
            case DummyAction.Jump:
                return Buttons.Up;
            case DummyAction.BlockAll:
                return Guard(state, player);
            case DummyAction.BlockAfterFirstHit:
                ref readonly var me = ref state.Fighters[player];
                if (me.Hitstun > 0 || me.Blockstun > 0)
                    _guardUntil = state.Frame + GuardHoldFrames;
                return state.Frame <= _guardUntil ? Guard(state, player) : Buttons.None;
            case DummyAction.Playback:
                return Play(state.Fighters[player].Facing);
            default:
                return Buttons.None;
        }
    }

    // While an attack is coming (in its startup or active frames, not yet connected) or the dummy
    // is in blockstun: back, away from the attacker by position (so a crossup is guarded the right
    // way), and down too against a low. Otherwise nothing, so it stands its ground rather than
    // walking back.
    public static Buttons Guard(in GameState state, int player)
    {
        ref readonly var me = ref state.Fighters[player];
        ref readonly var them = ref state.Fighters[1 - player];
        var move = them.Action == (int)FighterAction.Attack && them.Move >= 0 ? Characters.Characters.Placeholder.Moves[them.Move] : null;
        var coming = move is not null && !them.MoveHit && them.ActionFrame < move.FirstActiveFrame + move.Active;
        if (!coming && me.Blockstun == 0)
            return Buttons.None;

        var awayIsRight = me.Position.X != them.Position.X ? me.Position.X > them.Position.X : me.Facing < 0;
        var back = awayIsRight ? Buttons.Right : Buttons.Left;
        return move?.Guard == Characters.Guard.Low ? back | Buttons.Down : back;
    }

    private Buttons Play(int facing)
    {
        if (_playbackFrame >= Recording.Frames)
        {
            if (!Loop || Recording.Frames == 0)
                return Buttons.None;
            _playbackFrame = 0;
        }
        return Recording.At(_playbackFrame++, facing);
    }
}
