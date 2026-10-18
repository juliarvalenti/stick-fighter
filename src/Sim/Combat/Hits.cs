using StickFighter.Sim.Collision;
using StickFighter.Sim.Numerics;

namespace StickFighter.Sim.Combat;

// Applies the frame's contacts, all found before any is applied, so trades hit both fighters and
// neither player wins a tie by being player 1. A contact is a fighter's hitbox on the other's body,
// a projectile on a fighter (it uses the move that threw it, and is spent), or two projectiles
// meeting (both cancel out).
public static class Hits
{
    public static void Apply(ref GameState state, in Contacts contacts)
    {
        var character = Characters.Characters.Placeholder;

        // Read both moves, and whether each fighter is guarding, before anything is applied: applying
        // one hit clears the defender's move, and in a trade that defender's own hit still has to land.
        var moves = new Pair(state.Fighters[0].Move, state.Fighters[1].Move);
        var guarding = new Pair(Guarding(state, 0, out var p1Crouching) ? 1 : 0, Guarding(state, 1, out var p2Crouching) ? 1 : 0);
        var crouching = new Pair(p1Crouching ? 1 : 0, p2Crouching ? 1 : 0);

        // Likewise each fighter's stance before any hit: in its own attack's startup (a hit there is
        // a counter hit), and crouching (for the crouch hitstun bonus).
        var startup = new Pair(HitStates.InStartup(state.Fighters[0], character) ? 1 : 0, HitStates.InStartup(state.Fighters[1], character) ? 1 : 0);
        var low = new Pair(HitStates.Crouching(state.Fighters[0], character) ? 1 : 0, HitStates.Crouching(state.Fighters[1], character) ? 1 : 0);
        var before = new Before(guarding, crouching, startup, low);

        // The longest hitstop among this frame's hits and blocks: in a trade both freeze for it.
        var freeze = 0;

        // Two fireballs meeting cancel out, before either can reach a fighter.
        for (var c = 0; c < contacts.Count; c++)
        {
            var contact = contacts[c];
            if (contact.Kind != ContactKind.ProjectileClash)
                continue;
            // One event per fireball, at its own centre, so each breaks up where it is, in its thrower's
            // colour (the two are a radius or so apart when they touch).
            var a = state.Projectiles[contact.Attacker];
            var b = state.Projectiles[contact.Defender];
            state.Projectiles[contact.Attacker].Active = false;
            state.Projectiles[contact.Defender].Active = false;
            SimEvents.Add(ref state, EventKind.Clash, a.Owner, b.Owner, a.Move, a.Position);
            SimEvents.Add(ref state, EventKind.Clash, b.Owner, a.Owner, b.Move, b.Position);
        }

        for (var c = 0; c < contacts.Count; c++)
        {
            var contact = contacts[c];
            if (contact.Kind == ContactKind.Hit)
            {
                var moveIndex = moves[contact.Attacker];
                if (moveIndex < 0)
                    continue;
                var key = ComboKey(state.Fighters[contact.Attacker].LastMoveFrame, contact.Attacker, projectile: false);
                if (Land(ref state, contact.Attacker, contact.Defender, moveIndex, contact.Point, before, key, pushFrom: null, ref freeze))
                    state.Fighters[contact.Attacker].MoveHit = true;
            }
            else if (contact.Kind == ContactKind.ProjectileHit)
            {
                ref var projectile = ref state.Projectiles[contact.Attacker];
                if (!projectile.Active)
                    continue; // cancelled by the other fireball this frame
                var spawned = state.Frame - projectile.Frame;
                var key = ComboKey(spawned, projectile.Owner, projectile: true);
                var direction = projectile.Velocity.X >= Fix.Zero ? 1 : -1;
                if (Land(ref state, projectile.Owner, contact.Defender, projectile.Move, contact.Point, before, key, pushFrom: direction, ref freeze))
                    state.Projectiles[contact.Attacker].Active = false;
            }
        }

        Hitstop.Start(ref state, freeze);
    }

    // Which move instance hit, for combo scaling counted per move: the frame it started (or the
    // projectile was thrown), the attacker, and whether it's the projectile.
    private static int ComboKey(int started, int attacker, bool projectile) => started * 4 + (projectile ? 2 : 0) + attacker;

    // Lands one hit or block of `moveIndex` by `attackerIndex` on `defenderIndex`. A fighter's own hit
    // pushes the defender back (and the attacker, in the corner); a projectile's (pushFrom: its
    // direction of travel) pushes only the defender, so a fireball never drags its thrower. Returns
    // false if the move couldn't touch the defender (down, or falling past what it can juggle).
    private static bool Land(ref GameState state, int attackerIndex, int defenderIndex, int moveIndex, FixVec2 point,
        in Before before, int comboKey, int? pushFrom, ref int freeze)
    {
        var character = Characters.Characters.Placeholder;
        var stats = character.Stats;
        ref var attacker = ref state.Fighters[attackerIndex];
        ref var defender = ref state.Fighters[defenderIndex];
        if (defender.KnockedDown)
            return false;
        var move = character.Moves[moveIndex];

        // A juggled fighter passes through moves that can't juggle it; the move can still hit later.
        if (!HitStates.CanHit(move, defender))
            return false;

        var crouching = before.Crouching[defenderIndex] == 1;
        if (before.Guarding[defenderIndex] == 1 && Blocking.RightHeight(move.Guard, crouching))
        {
            var healthBefore = defender.Health;
            Block(ref defender, move, crouching);
            Push(ref attacker, ref defender, Fix.FromInt(move.PushbackBlock), state.Stage, pushFrom);
            freeze = move.HitstopOnBlock > freeze ? move.HitstopOnBlock : freeze;
            SimEvents.Add(ref state, EventKind.Block, attackerIndex, defenderIndex, moveIndex, point);
            if (healthBefore > 0 && defender.Health == 0)
                SimEvents.Add(ref state, EventKind.Ko, attackerIndex, defenderIndex, moveIndex, point); // a chip KO
            return true;
        }
        freeze = move.HitstopOnHit > freeze ? move.HitstopOnHit : freeze;

        // The hit's place in the defender's combo: it continues one still running (the last hit's
        // hitstun or juggle fall not over), or starts a new one. Scaling counts moves, not hits, so a
        // move that hits more than once is one step.
        var hitInCombo = defender.ComboHits + 1;
        if (defender.ComboHits == 0 || defender.ComboKey != comboKey)
        {
            defender.ComboMoves++;
            defender.ComboKey = comboKey;
        }
        var counter = before.Startup[defenderIndex] == 1;
        var damage = HitStates.Damage(move.Damage, defender.ComboMoves, counter, defender.Health, stats.Health, stats.LowLifeReduction);
        damage = damage < defender.Health ? damage : defender.Health;

        defender.Blockstun = 0;
        defender.Health -= damage;
        defender.ComboHits = hitInCombo;
        defender.ComboDamage += damage;
        defender.CounterHit = counter;
        SimEvents.Add(ref state, counter ? EventKind.CounterHit : EventKind.Hit, attackerIndex, defenderIndex, moveIndex, point);
        if (damage > 0 && defender.Health == 0) // only the hit that takes the last point is the KO
            SimEvents.Add(ref state, EventKind.Ko, attackerIndex, defenderIndex, moveIndex, point);
        defender.Stun += HitStates.Stun(move.Stun, defender.ComboMoves, counter);
        defender.StunRecovery = stats.StunRecoveryDelay;
        defender.Dizzy = 0; // a hit ends a dizzy (the gauge was already reset)

        // Hitstun counts the frame of the hit, so the defender acts again move.Hitstun frames after
        // the hit lands, and on-hit advantage is exactly hitstun - active - recovery. A counter hit
        // and a crouching defender add to it.
        defender.Hitstun = move.Hitstun - 1
            + (counter ? HitStates.CounterHitstun(move.Strength) : 0)
            + (before.Low[defenderIndex] == 1 ? stats.CrouchHitstunBonus : 0);
        defender.Action = (int)Characters.FighterAction.Hitstun;
        defender.ActionFrame = 0;
        defender.Move = -1;
        defender.Velocity = FixVec2.Zero;

        // In the air, the hit knocks the defender into a fall: up a little and away, then gravity.
        if (defender.Airborne)
        {
            defender.Juggle = defender.Juggle == 0 ? move.JuggleStart : defender.Juggle + move.JuggleIncrease;
            var away = pushFrom ?? (defender.Position.X >= attacker.Position.X ? 1 : -1);
            defender.Velocity = new FixVec2(stats.AirHitDrift * away, -stats.AirHitRise);
        }
        else if (move.HardKnockdown)
        {
            // A sweep: straight down, no hitstun and no quick rise.
            Knockdowns.KnockDown(ref defender, stats.KnockdownFrames, stats);
            SimEvents.Add(ref state, EventKind.Knockdown, attackerIndex, defenderIndex, moveIndex, defender.Position);
        }

        Push(ref attacker, ref defender, Fix.FromInt(move.PushbackHit), state.Stage, pushFrom);
        return true;
    }

    // Holding back (away from the attacker, by position) while free to guard.
    private static bool Guarding(in GameState state, int player, out bool crouching)
    {
        crouching = Blocking.HoldingDown(state, player);
        return Blocking.CanGuard(state.Fighters[player]) && Blocking.HoldingBack(state, player);
    }

    // Blocked: blockstun (counting the frame of contact, like hitstun, so on-block advantage is
    // exactly blockstun - active - recovery), and chip. Chip from a move that can't chip-KO leaves at
    // least 1 health. (Normals have no chip; specials do.)
    private static void Block(ref FighterState defender, Characters.Move move, bool crouching)
    {
        defender.Blockstun = move.Blockstun - 1;
        defender.Action = (int)(crouching ? Characters.FighterAction.BlockCrouch : Characters.FighterAction.BlockStand);
        defender.ActionFrame = 0;
        defender.Velocity = FixVec2.Zero;
        defender.Health = AfterChip(defender.Health, move.Chip, move.ChipKo);
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

    // The defender slides straight back, away from the attacker (or along a projectile's travel,
    // pushFrom). If a wall stops it short, the attacker slides back by the rest instead (SF4's corner
    // pushback), so a cornered defender can't be pinned; a projectile's thrower is never moved. The
    // separation limit is applied afterwards by Pushing, from each fighter's real movement this
    // frame, so it only ever pulls back whoever moved away.
    private static void Push(ref FighterState attacker, ref FighterState defender, Fix distance, in Stage stage, int? pushFrom)
    {
        var away = pushFrom ?? (defender.Position.X >= attacker.Position.X ? 1 : -1);

        var defenderRoom = away > 0 ? Pushing.MaxX(stage) - defender.Position.X : defender.Position.X - Pushing.MinX(stage);
        var defenderMove = Fix.Clamp(distance, Fix.Zero, Fix.Max(Fix.Zero, defenderRoom));
        defender.Position += new FixVec2(defenderMove * away, Fix.Zero);

        var rest = distance - defenderMove;
        if (rest <= Fix.Zero || pushFrom is not null)
            return;
        var attackerRoom = away > 0 ? attacker.Position.X - Pushing.MinX(stage) : Pushing.MaxX(stage) - attacker.Position.X;
        var attackerMove = Fix.Clamp(rest, Fix.Zero, Fix.Max(Fix.Zero, attackerRoom));
        attacker.Position -= new FixVec2(attackerMove * away, Fix.Zero);
    }

    // A value per player, read before anything is applied.
    private readonly struct Pair
    {
        private readonly int _p1;
        private readonly int _p2;

        public Pair(int p1, int p2)
        {
            _p1 = p1;
            _p2 = p2;
        }

        public int this[int player] => player == 0 ? _p1 : _p2;
    }

    private readonly record struct Before(Pair Guarding, Pair Crouching, Pair Startup, Pair Low);
}
