using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace StickFighter.Sim.Replays;

// Which GameState fields differ between two states, as readable lines ("Fighters[1].Hitstun: 7 vs 6"),
// for when a checksum says two states differ but not how. Diagnostics only: it reads fields by
// reflection, in declaration order, so a field added to GameState or anything in it shows up here
// with no change. The inline arrays (inputs, fighters, projectiles) are walked by index.
public static class StateDiff
{
    public static List<string> Between(in GameState expected, in GameState actual)
    {
        var lines = new List<string>();
        foreach (var field in PublicFields(typeof(GameState)))
        {
            if (field.FieldType == typeof(InputHistories))
            {
                for (var p = 0; p < GameState.PlayerCount; p++)
                {
                    for (var slot = 0; slot < InputHistory.Depth; slot++)
                        Compare($"{field.Name}[{p}][{slot}]", expected.Inputs[p][slot], actual.Inputs[p][slot], lines);
                }
            }
            else if (field.FieldType == typeof(FighterPair))
            {
                for (var p = 0; p < GameState.PlayerCount; p++)
                    Compare($"{field.Name}[{p}]", expected.Fighters[p], actual.Fighters[p], lines);
            }
            else if (field.FieldType == typeof(ProjectileSlots))
            {
                for (var i = 0; i < GameState.MaxProjectiles; i++)
                    Compare($"{field.Name}[{i}]", expected.Projectiles[i], actual.Projectiles[i], lines);
            }
            else
            {
                Compare(field.Name, field.GetValue(expected)!, field.GetValue(actual)!, lines);
            }
        }
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
