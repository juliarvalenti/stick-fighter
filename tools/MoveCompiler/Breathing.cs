using System;
using System.Text;
using StickFighter.Sim.Characters;

namespace StickFighter.MoveCompiler;

// The idle: standing and crouching breathe. The hip settles a couple of px and comes back over
// 40 frames with the feet planted, so a fighter never stands perfectly still.
//
// Generated from each stance's first key (the pose moves start from) and written into the
// character's own file, so nobody keys it by hand; `--check` fails if the file is out of date.
public static class Breathing
{
    public const int Length = 40; // frames per breath
    public const double Depth = 2; // px the hip settles at the bottom of a breath
    private const int Step = 5; // frames between keys

    private static readonly (string Name, FighterAction Action)[] Stances =
    {
        ("stand", FighterAction.Stand),
        ("crouch", FighterAction.Crouch),
    };

    // The character file with its stand and crouch tracks regenerated.
    public static string Apply(string characterJson)
    {
        var character = CharacterLoader.Load(characterJson, null, "character");
        var rig = new Rig(character.Skeleton);
        foreach (var (name, action) in Stances)
        {
            // A stance's own pushbox height is kept; one that uses the standing default stays without.
            var pushHeight = character.PushHeight(action);
            int? own = pushHeight == character.Stats.PushHeight ? null : pushHeight.RoundToInt();
            characterJson = Replace(characterJson, name, Track(name, FloatPose.From(character.Pose(action).Keys[0]), own, rig));
        }
        return characterJson;
    }

    private static string Track(string name, FloatPose pose, int? pushHeight, Rig rig)
    {
        var back = rig.Leg(pose, front: false).End;
        var front = rig.Leg(pose, front: true).End;
        var text = new StringBuilder();
        text.Append('"').Append(name).Append("\": {\n");
        text.Append("      \"length\": ").Append(Length).Append(",\n");
        if (pushHeight is { } height)
            text.Append("      \"pushHeight\": ").Append(height).Append(",\n");
        text.Append("      \"keys\": [\n");
        int[]? previous = null;
        var first = true;
        for (var frame = 0; frame < Length; frame += Step)
        {
            // Down and back up along a cosine, rounded to whole px.
            var settle = Math.Round(Depth * (1 - Math.Cos(2 * Math.PI * frame / Length)) / 2, MidpointRounding.AwayFromZero);
            var key = pose.Clone();
            if (settle != 0)
            {
                key.HipY += settle;
                if (!rig.Solve(key, arm: false, front: false, back) || !rig.Solve(key, arm: false, front: true, front))
                    throw new CompileException($"{name}: the feet can't stay planted while breathing");
            }
            var values = Output.Round(key);
            if (previous is not null && Output.Same(previous, values))
                continue;
            if (!first)
                text.Append(",\n");
            first = false;
            text.Append("        ").Append(Output.Key(frame, values));
            previous = values;
        }
        text.Append("\n      ]\n    }");
        return text.ToString();
    }

    // Swaps one stance's block (`"name": { ... }` at the poses' indent) for `block`.
    private static string Replace(string json, string name, string block)
    {
        var start = json.IndexOf($"\"{name}\": {{", StringComparison.Ordinal);
        var end = start < 0 ? -1 : json.IndexOf("\n    }", start, StringComparison.Ordinal);
        if (end < 0)
            throw new CompileException($"the character file has no \"{name}\" pose block to write the breathing into");
        return string.Concat(json.AsSpan(0, start), block, json.AsSpan(end + "\n    }".Length));
    }
}
