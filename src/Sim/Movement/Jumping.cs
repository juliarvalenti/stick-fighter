using StickFighter.Sim.Numerics;

namespace StickFighter.Sim.Movement;

// The jump arc: each airborne frame the fighter moves by its velocity, then gravity pulls the
// velocity down. Integer fixed-point all the way, so an arc lands on the same frame and pixel on
// every machine. Y grows downward, so a jump starts with a negative vertical velocity.
public static class Jumping
{
    // Takeoff velocity for a jump: up at the character's jump speed, and sideways by its forward or
    // back drift (`direction` is -1 back, 0 neutral, +1 forward, relative to facing).
    public static FixVec2 Takeoff(Characters.Stats stats, int direction, int facing) =>
        new(
            direction > 0 ? stats.JumpForward * facing : direction < 0 ? -stats.JumpBack * facing : Fix.Zero,
            -stats.JumpVelocity);

    // One airborne frame. Returns true if the fighter touched down this frame, in which case it's
    // put on the floor with no velocity.
    public static bool Fall(ref FighterState fighter, Fix floor, Fix gravity)
    {
        fighter.Position += fighter.Velocity;
        fighter.Velocity = new FixVec2(fighter.Velocity.X, fighter.Velocity.Y + gravity);
        if (fighter.Position.Y < floor)
            return false;
        fighter.Position = new FixVec2(fighter.Position.X, floor);
        fighter.Velocity = FixVec2.Zero;
        return true;
    }

    // Frames from takeoff until the fighter touches down (the touchdown frame included), by the same
    // steps as Fall. The loader checks this against the character's airborneFrames.
    public static int ArcFrames(Fix jumpVelocity, Fix gravity)
    {
        var y = Fix.Zero;
        var vy = -jumpVelocity;
        for (var frame = 1; frame <= 10_000; frame++)
        {
            y += vy;
            vy += gravity;
            if (y >= Fix.Zero)
                return frame;
        }
        return int.MaxValue;
    }
}
