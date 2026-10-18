using Godot;
using StickFighter.Sim;
using StickFighter.Style;

namespace StickFighter.Menus;

// What a match starts with, as chosen on the menus: the mode and each player's color (0 is the
// player's own color, 1 their alternate). With one character so far there's no character choice to
// carry; it joins here with the second character.
public sealed record MatchSettings(MatchMode Mode, int P1Color = 0, int P2Color = 0)
{
    public static readonly MatchSettings Training = new(MatchMode.Training);

    // The two fighters' colors. The players' own colors stay the game's current ones until Julia's
    // palette call; the alternates are the style guide's.
    public Color[] Colors(Color p1, Color p2) => new[]
    {
        P1Color == 0 ? p1 : Look.P1Alt,
        P2Color == 0 ? p2 : Look.P2Alt,
    };
}
