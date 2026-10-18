using StickFighter.Sim.Numerics;

namespace StickFighter.Sim.Collision;

// The placeholder stage: one screen wide, with walls at both edges.
public static class Stage
{
    public static readonly Fix LeftWall = Fix.Zero;
    public static readonly Fix RightWall = Fix.FromInt(1280);
    public static readonly Fix Floor = Fix.FromInt(600);
}
