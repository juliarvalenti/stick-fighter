using StickFighter.Sim.Collision;
using StickFighter.Sim.Numerics;

namespace StickFighter.Sim.Combat;

// Applies the frame's contacts, all found before any is applied, so trades hit both fighters and
// neither player wins a tie by being player 1.
public static class Hits
{
    public static void Apply(ref GameState state, in Contacts contacts)
    {
        var character = Characters.Characters.Placeholder;

        // Read both moves first: applying one hit clears the defender's move, and in a trade that
        // defender's own hit still has to land.
        var p1Move = state.Fighters[0].Move;
        var p2Move = state.Fighters[1].Move;

        for (var c = 0; c < contacts.Count; c++)
        {
            var contact = contacts[c];
            if (contact.Kind != ContactKind.Hit)
                continue; // projectiles land in Specials, cancels, links

            ref var attacker = ref state.Fighters[contact.Attacker];
            ref var defender = ref state.Fighters[contact.Defender];
            var moveIndex = contact.Attacker == 0 ? p1Move : p2Move;
            if (moveIndex < 0 || defender.KnockedDown)
                continue;
            var move = character.Moves[moveIndex];

            attacker.MoveHit = true;

            defender.Health = defender.Health > move.Damage ? defender.Health - move.Damage : 0;

            // Hitstun counts the frame of the hit, so the defender acts again move.Hitstun frames
            // after the hit lands, and on-hit advantage is exactly hitstun - active - recovery.
            defender.Hitstun = move.Hitstun - 1;
            defender.Action = (int)Characters.FighterAction.Hitstun;
            defender.ActionFrame = 0;
            defender.Move = -1;
            defender.Velocity = FixVec2.Zero;

            // Pushed straight back, away from the attacker. Walls stop it (Pushing runs next frame).
            var away = defender.Position.X >= attacker.Position.X ? 1 : -1;
            defender.Position += new FixVec2(Fix.FromInt(move.PushbackHit * away), Fix.Zero);
        }
    }
}
