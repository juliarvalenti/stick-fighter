using StickFighter.Sim.Numerics;

namespace StickFighter.Sim.Collision;

// The placeholder fighter's skeleton and the shapes that come from it. One fixed standing pose
// until moves supply keyframed poses and hitbox frames (Movement, normals, blocking, throws).
// Offsets are in px from the feet, for a fighter facing right; facing left mirrors X.
public static class Body
{
    public static readonly Fix PushHalfWidth = Fix.FromInt(30);
    public static readonly Fix PushHeight = Fix.FromInt(230);

    private static readonly Fix LimbRadius = Fix.FromInt(8);
    private static readonly Fix TorsoRadius = Fix.FromInt(16);
    private static readonly Fix HeadRadius = Fix.FromInt(28);

    // Joints of the standing pose.
    public static readonly FixVec2 Hip = V(0, -110);
    public static readonly FixVec2 Neck = V(0, -230);
    public static readonly FixVec2 Head = V(0, -266);
    public static readonly FixVec2 BackFoot = V(-35, 0);
    public static readonly FixVec2 FrontFoot = V(35, 0);
    public static readonly FixVec2 BackHand = V(-40, -160);
    public static readonly FixVec2 FrontHand = V(70, -210);
    public static readonly Fix HeadSize = HeadRadius;

    // A joint's position in the world for this fighter.
    public static FixVec2 Joint(in FighterState fighter, FixVec2 offset) =>
        fighter.Position + new FixVec2(offset.X * fighter.Facing, offset.Y);

    public static FighterShapes Shapes(in FighterState fighter)
    {
        var shapes = new FighterShapes();
        var hip = Joint(fighter, Hip);
        var neck = Joint(fighter, Neck);

        shapes.AddHurt(Capsule.Circle(Joint(fighter, Head), HeadRadius));
        shapes.AddHurt(new Capsule(hip, neck, TorsoRadius));
        shapes.AddHurt(new Capsule(hip, Joint(fighter, BackFoot), LimbRadius));
        shapes.AddHurt(new Capsule(hip, Joint(fighter, FrontFoot), LimbRadius));
        shapes.AddHurt(new Capsule(neck, Joint(fighter, BackHand), LimbRadius));
        shapes.AddHurt(new Capsule(neck, Joint(fighter, FrontHand), LimbRadius));

        shapes.Push = PushBox(fighter.Position);
        return shapes;
    }

    public static Box PushBox(FixVec2 feet) => new(
        new FixVec2(feet.X - PushHalfWidth, feet.Y - PushHeight),
        new FixVec2(feet.X + PushHalfWidth, feet.Y));

    private static FixVec2 V(int x, int y) => new(Fix.FromInt(x), Fix.FromInt(y));
}
