using StickFighter.Sim.Characters;
using StickFighter.Sim.Numerics;

namespace StickFighter.Sim.Movement;

// Dashes: 66 and 44, a fixed distance on a curve, then recovery.
public static class Dashing
{
    // +1 for a forward dash (66), -1 for a back dash (44), 0 for neither: the direction is pressed
    // on this frame and was also pressed within `window` frames before it (a release between the
    // two is implied by the second press). Up or down held makes it a jump or crouch, not a dash.
    public static int Detect(in InputHistory history, int now, int facing, int window)
    {
        var held = history.At(now, now);
        if (held.HasAny(Buttons.Up | Buttons.Down))
            return 0;
        var forward = facing > 0 ? Buttons.Right : Buttons.Left;
        var back = facing > 0 ? Buttons.Left : Buttons.Right;
        if (DoubleTapped(history, now, forward, window))
            return 1;
        if (DoubleTapped(history, now, back, window))
            return -1;
        return 0;
    }

    private static bool DoubleTapped(in InputHistory history, int now, Buttons direction, int window)
    {
        if (!history.PressedOn(now, now).Has(direction))
            return false;
        for (var frame = now - 1; frame >= now - window; frame--)
        {
            if (history.PressedOn(frame, now).Has(direction))
                return true;
        }
        return false;
    }

    // How far into the dash the fighter is after `frames` frames of it: the distance covered over
    // the moving frames on a curve that starts fast and eases to a stop (each frame's step one
    // smaller than the last), then nothing during recovery. Worked from the cumulative distance,
    // so the steps always add up to exactly `distance`.
    public static Fix Offset(int frames, int moving, Fix distance)
    {
        if (frames <= 0)
            return Fix.Zero;
        if (frames >= moving)
            return distance;
        long total = (long)moving * (moving + 1) / 2;
        long covered = (long)frames * moving - (long)frames * (frames - 1) / 2;
        return Fix.FromRaw(distance.Raw * covered / total);
    }

    // The dash's step on this frame (frame 0 is the first), along the fighter's facing for a forward
    // dash and against it for a back dash.
    public static Fix Step(Stats stats, bool forward, int frame)
    {
        var (frames, recovery, distance) = forward
            ? (stats.ForwardDashFrames, stats.ForwardDashRecovery, stats.ForwardDashDistance)
            : (stats.BackDashFrames, stats.BackDashRecovery, stats.BackDashDistance);
        var moving = frames - recovery;
        return Offset(frame + 1, moving, distance) - Offset(frame, moving, distance);
    }

    public static int Frames(Stats stats, bool forward) => forward ? stats.ForwardDashFrames : stats.BackDashFrames;

    public static bool IsDashing(in FighterState fighter) =>
        fighter.Action is (int)FighterAction.DashForward or (int)FighterAction.DashBack;

    // The back-dash's first frames have no hurtboxes and can't be thrown.
    public static bool Invincible(in FighterState fighter, Stats stats) =>
        fighter.Action == (int)FighterAction.DashBack && fighter.ActionFrame < stats.BackDashInvincibleFrames;
}
