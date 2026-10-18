using System.Numerics;
using StickFighter.Sim.Characters;
using StickFighter.Sim.Numerics;

namespace StickFighter.Sim.Combat;

// Knockdowns and the stun gauge, from SF4.
// - Hard knockdown: a sweep (Move.HardKnockdown) on a grounded fighter, or a throw. Down for
//   KnockdownFrames, no quick rise.
// - Soft knockdown: landing from a juggle fall (any air hit). Down for KnockdownFrames, or
//   QuickRiseFrames if down or two attack buttons were pressed in the last QuickRiseWindow frames.
// - Nothing hits a fighter who is down (Hits), and getting up gives a little throw invulnerability.
// - The stun gauge resets on a knockdown, unless it's already full: then the fighter gets up dizzy.
public static class Knockdowns
{
    public static void KnockDown(ref FighterState fighter, int frames, Stats stats)
    {
        fighter.KnockedDown = true;
        fighter.Knockdown = frames;
        fighter.Action = (int)FighterAction.Knockdown;
        fighter.ActionFrame = 0;
        fighter.Move = -1;
        fighter.Hitstun = 0;
        fighter.Blockstun = 0;
        fighter.Juggle = 0;
        fighter.Dizzy = 0;
        fighter.Velocity = FixVec2.Zero;
        if (fighter.Stun < stats.Stun)
            fighter.Stun = 0;
    }

    // Down, or two different attack buttons, pressed in the last `window` frames (counting now).
    public static bool QuickRise(in InputHistory history, int now, int window)
    {
        var buttons = Buttons.None;
        for (var frame = now; frame > now - window; frame--)
        {
            var pressed = history.PressedOn(frame, now);
            if (pressed.Has(Buttons.Down))
                return true;
            buttons |= pressed & Buttons.Attacks;
        }
        return BitOperations.PopCount((uint)buttons) >= 2;
    }

    // A frame of the stun gauge: after StunRecoveryDelay frames without a hit, it drains by
    // StunRecoveryRate a frame. A full gauge doesn't drain: it's waiting to make the fighter dizzy.
    public static void RecoverStun(ref FighterState fighter, Stats stats)
    {
        if (fighter.StunRecovery > 0)
        {
            fighter.StunRecovery--;
            return;
        }
        if (fighter.Stun > 0 && fighter.Stun < stats.Stun)
            fighter.Stun = fighter.Stun > stats.StunRecoveryRate ? fighter.Stun - stats.StunRecoveryRate : 0;
    }
}
