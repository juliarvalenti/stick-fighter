using System;
using System.Collections.Generic;

namespace StickFighter.UI;

public enum GlyphKind
{
    Air, // "j.": done in the air
    Direction, // a numpad direction, 1 to 9 (5 is neutral), facing right
    Button, // an attack button
    Plus, // pressed together
}

public enum Attack
{
    Punch,
    Kick,
}

public enum Strength
{
    Any, // "P": any punch
    Light,
    Medium,
    Heavy,
}

public readonly record struct Glyph(GlyphKind Kind, int Direction = 5, Attack Attack = Attack.Punch, Strength Strength = Strength.Any);

// Reads numpad notation into the glyphs to draw: `236P` is down, down-forward, forward, punch;
// `j.HK` is heavy kick in the air; `LP+LK` is both together. Plain C# so it's tested without Godot
// (tests/Presentation.Tests), against every move the characters have.
public static class Notation
{
    public static IReadOnlyList<Glyph> Parse(string input)
    {
        var glyphs = new List<Glyph>();
        var i = 0;
        if (input.StartsWith("j.", StringComparison.Ordinal))
        {
            glyphs.Add(new Glyph(GlyphKind.Air));
            i = 2;
        }

        while (i < input.Length && input[i] is >= '1' and <= '9')
            glyphs.Add(new Glyph(GlyphKind.Direction, input[i++] - '0'));

        var buttons = 0;
        while (i < input.Length)
        {
            if (buttons > 0)
            {
                if (input[i] != '+')
                    throw new FormatException($"'{input}': expected '+' between buttons at {i}");
                glyphs.Add(new Glyph(GlyphKind.Plus));
                i++;
            }

            var strength = Strength.Any;
            if (i < input.Length && input[i] is 'L' or 'M' or 'H')
            {
                strength = input[i] switch { 'L' => Strength.Light, 'M' => Strength.Medium, _ => Strength.Heavy };
                i++;
            }
            if (i >= input.Length || input[i] is not ('P' or 'K'))
                throw new FormatException($"'{input}': expected P or K at {i}");
            glyphs.Add(new Glyph(GlyphKind.Button, Attack: input[i] == 'P' ? Attack.Punch : Attack.Kick, Strength: strength));
            i++;
            buttons++;
        }

        if (buttons == 0)
            throw new FormatException($"'{input}': no button");
        return glyphs;
    }

    // The arrow for a numpad direction, facing right, with y pointing down the screen: (0, 0) for 5.
    public static (int X, int Y) Vector(int direction) => ((direction - 1) % 3 - 1, 1 - (direction - 1) / 3);
}
