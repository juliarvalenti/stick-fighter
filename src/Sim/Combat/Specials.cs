using StickFighter.Sim.Characters;

namespace StickFighter.Sim.Combat;

// Special moves: for now the fireball, 236 + P. A special comes out ahead of the normal its button
// would give (236 + LP is a fireball, not a jab), from a press in the buffer window or, for negative
// edge, a release. The punch picks its strength: LP, MP or HP, for the projectile's speed.
public static class Specials
{
    public static readonly int Fireball = IndexOf("236P");

    // The press (or release) that completes a fireball within the buffer window, earliest first like
    // the normals' buffer, as the strength (0, 1, 2 for LP, MP, HP; the heaviest if several), or -1.
    // Not while this player's fireball is still on screen: one each.
    public static int FireballStrength(in GameState state, int player, int lastMoveFrame)
    {
        if (Fireball < 0 || Projectiles.HasOneOut(state, player))
            return -1;
        ref readonly var fighter = ref state.Fighters[player];
        var history = state.Inputs[player];
        var stats = Characters.Characters.Placeholder.Stats;
        var now = state.Frame;
        var oldest = Hitstop.WindowStart(state, now, Normals.BufferWindow);
        for (var frame = oldest > lastMoveFrame ? oldest : lastMoveFrame + 1; frame <= now; frame++)
        {
            var punches = (history.PressedOn(frame, now) | history.ReleasedOn(frame, now)) & Buttons.Punches;
            if (punches == Buttons.None)
                continue;
            // The motion's window and the button's leniency count unfrozen frames, like the buffer.
            var start = Hitstop.WindowStart(state, frame, stats.MotionWindow);
            var forwardStart = Hitstop.WindowStart(state, frame, stats.MotionButtonLeniency + 1);
            if (Motions.QuarterCircleForward(history, frame, now, fighter.Facing, start, forwardStart))
                return punches.Has(Buttons.HeavyPunch) ? 2 : punches.Has(Buttons.MediumPunch) ? 1 : 0;
        }
        return -1;
    }

    private static int IndexOf(string input)
    {
        var moves = Characters.Characters.Placeholder.Moves;
        for (var i = 0; i < moves.Length; i++)
        {
            if (moves[i].Input == input)
                return i;
        }
        return -1;
    }
}
