using Godot;
using StickFighter.Sim.Numerics;

namespace StickFighter;

// One-way conversions from simulation numbers to Godot's floats, for drawing only.
// Nothing converted here may flow back into the simulation.
public static class FixConversions
{
    public static float ToFloat(this Fix value) => value.Raw / (float)Fix.OneRaw;

    public static Vector2 ToVector2(this FixVec2 value) => new(value.X.ToFloat(), value.Y.ToFloat());

    public static float ToRadians(this Angle angle) => angle.Units * Mathf.Tau / Angle.UnitsPerTurn;
}
