using System.Collections.Generic;
using StickFighter.Sim;
using StickFighter.Sim.Combat;

namespace StickFighter.Menus;

// The moves a player can actually do right now: the ones the sim's own input tables reach. That's
// every normal (each attack button, standing, crouching and in the air) and the throw. A move in the
// data with no way in yet (the fireball, 236P, until Specials adds motion inputs) isn't listed, so
// the move list never promises what the game can't do. When Specials adds its table, add it here.
public static class Triggerable
{
    private static readonly Buttons[] Attacks =
    {
        Buttons.LightPunch, Buttons.MediumPunch, Buttons.HeavyPunch, Buttons.LightKick, Buttons.MediumKick, Buttons.HeavyKick,
    };

    public static IReadOnlySet<int> Moves => _moves ??= Build();
    private static HashSet<int>? _moves;

    private static HashSet<int> Build()
    {
        var moves = new HashSet<int>();
        foreach (var stance in new[] { NormalStance.Standing, NormalStance.Crouching, NormalStance.Airborne })
        {
            foreach (var button in Attacks)
            {
                if (Normals.Pick(button, stance) is var move and >= 0)
                    moves.Add(move);
            }
        }
        if (Normals.Throw >= 0)
            moves.Add(Normals.Throw);
        return moves;
    }
}
