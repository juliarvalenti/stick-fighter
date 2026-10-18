using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace StickFighter.Sim.Replays;

// Which GameState fields differ between two states, as readable lines ("Fighters[1].Hitstun: 7 vs 6"),
// for when a checksum says two states differ but not how. Diagnostics only: it reads fields by
// reflection, in declaration order, so a field added to FighterState shows up here with no change.
public static class StateDiff
{
    public static List<string> Between(in GameState expected, in GameState actual)
    {
        var lines = new List<string>();
        Compare("Frame", expected.Frame, actual.Frame, lines);
        Compare("Rng", expected.Rng, actual.Rng, lines);
        for (var p = 0; p < GameState.PlayerCount; p++)
        {
            for (var slot = 0; slot < InputHistory.Depth; slot++)
                Compare($"Inputs[{p}][{slot}]", expected.Inputs[p][slot], actual.Inputs[p][slot], lines);
        }
        for (var p = 0; p < GameState.PlayerCount; p++)
            Compare($"Fighters[{p}]", expected.Fighters[p], actual.Fighters[p], lines);
        for (var i = 0; i < GameState.MaxProjectiles; i++)
            Compare($"Projectiles[{i}]", expected.Projectiles[i], actual.Projectiles[i], lines);
        Compare("Stage", expected.Stage, actual.Stage, lines);
        return lines;
    }

    // A struct with public fields is compared field by field (Position.X, Rng.State); anything
    // else (numbers, Fix, enums, button flags) as a whole value.
    private static void Compare(string name, object expected, object actual, List<string> lines)
    {
        var fields = PublicFields(expected.GetType());
        if (fields.Length == 0)
        {
            if (!expected.Equals(actual))
                lines.Add($"{name}: {expected} vs {actual}");
            return;
        }
        foreach (var field in fields)
            Compare($"{name}.{field.Name}", field.GetValue(expected)!, field.GetValue(actual)!, lines);
    }

    private static FieldInfo[] PublicFields(System.Type type) =>
        type.IsPrimitive || type.IsEnum || type == typeof(Numerics.Fix) // Fix prints as a number, not its raw bits
            ? System.Array.Empty<FieldInfo>()
            : type.GetFields(BindingFlags.Public | BindingFlags.Instance).OrderBy(f => f.MetadataToken).ToArray();
}
