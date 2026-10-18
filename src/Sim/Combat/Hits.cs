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

        // Read both moves, and whether each fighter is guarding, before anything is applied: applying
        // one hit clears the defender's move, and in a trade that defender's own hit still has to land.
        var p1Move = state.Fighters[0].Move;
        var p2Move = state.Fighters[1].Move;
        var p1Guarding = Guarding(state, 0, out var p1Crouching);
        var p2Guarding = Guarding(state, 1, out var p2Crouching);

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

            var guarding = contact.Defender == 0 ? p1Guarding : p2Guarding;
            var crouching = contact.Defender == 0 ? p1Crouching : p2Crouching;
            if (guarding && Blocking.RightHeight(move.Guard, crouching))
            {
                Block(ref attacker, ref defender, move, crouching, state.Stage);
                continue;
            }

            defender.Blockstun = 0;
            defender.Health = defender.Health > move.Damage ? defender.Health - move.Damage : 0;

            // Hitstun counts the frame of the hit, so the defender acts again move.Hitstun frames
            // after the hit lands, and on-hit advantage is exactly hitstun - active - recovery.
            defender.Hitstun = move.Hitstun - 1;
            defender.Action = (int)Characters.FighterAction.Hitstun;
            defender.ActionFrame = 0;
            defender.Move = -1;
            defender.Velocity = FixVec2.Zero;

            Push(ref attacker, ref defender, Fix.FromInt(move.PushbackHit), state.Stage);
        }
    }

    // Holding back (away from the attacker, by position) while free to guard.
    private static bool Guarding(in GameState state, int player, out bool crouching)
    {
        crouching = Blocking.HoldingDown(state, player);
        return Blocking.CanGuard(state.Fighters[player]) && Blocking.HoldingBack(state, player);
    }

    // Blocked: blockstun (counting the frame of contact, like hitstun, so on-block advantage is
    // exactly blockstun - active - recovery), pushback on block, and chip. Chip from a move that
    // can't chip-KO leaves at least 1 health.
    private static void Block(ref FighterState attacker, ref FighterState defender, Characters.Move move, bool crouching, in Stage stage)
    {
        defender.Blockstun = move.Blockstun - 1;
        defender.Action = (int)(crouching ? Characters.FighterAction.BlockCrouch : Characters.FighterAction.BlockStand);
        defender.ActionFrame = 0;
        defender.Velocity = FixVec2.Zero;

        defender.Health = AfterChip(defender.Health, move.Chip, move.ChipKo);
        Push(ref attacker, ref defender, Fix.FromInt(move.PushbackBlock), stage);
    }

    // Health after chip damage. A move that can't chip-KO never takes the last point.
    public static int AfterChip(int health, int chip, bool chipKo)
    {
        if (chip <= 0)
            return health;
        var floor = chipKo ? 0 : 1;
        var left = health - chip;
        return left > floor ? left : (health > floor ? floor : health);
    }

    // The defender slides straight back, away from the attacker. If a wall stops it short, the
    // attacker slides back by the rest instead (SF4's corner pushback), so a cornered defender can't
    // be pinned. The separation limit is applied afterwards by Pushing, from each fighter's real
    // movement this frame, so it only ever pulls back whoever moved away.
    private static void Push(ref FighterState attacker, ref FighterState defender, Fix distance, in Stage stage)
    {
        var away = defender.Position.X >= attacker.Position.X ? 1 : -1;

        var defenderRoom = away > 0 ? Pushing.MaxX(stage) - defender.Position.X : defender.Position.X - Pushing.MinX(stage);
        var defenderMove = Fix.Clamp(distance, Fix.Zero, Fix.Max(Fix.Zero, defenderRoom));
        defender.Position += new FixVec2(defenderMove * away, Fix.Zero);

        var rest = distance - defenderMove;
        if (rest <= Fix.Zero)
            return;
        var attackerRoom = away > 0 ? attacker.Position.X - Pushing.MinX(stage) : Pushing.MaxX(stage) - attacker.Position.X;
        var attackerMove = Fix.Clamp(rest, Fix.Zero, Fix.Max(Fix.Zero, attackerRoom));
        attacker.Position -= new FixVec2(attackerMove * away, Fix.Zero);
    }
}
