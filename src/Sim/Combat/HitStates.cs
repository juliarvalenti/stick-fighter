using StickFighter.Sim.Characters;

namespace StickFighter.Sim.Combat;

// What a hit does beyond the move's own numbers, from SF4: counter hits, combo damage scaling,
// extra hitstun on a crouching defender, low-life damage reduction, and whether a falling
// (juggled) fighter can be hit at all. Hits applies them; each rule is a small function here so
// it can be tested on its own.
public static class HitStates
{
    // Damage (and stun) scaling by the hit's place in the combo: 100%, 100%, 80%, 70%, 60% ...
    // down to a 10% floor.
    public static int ScalingPercent(int hitInCombo)
    {
        if (hitInCombo <= 2)
            return 100;
        var percent = 110 - 10 * hitInCombo;
        return percent > 10 ? percent : 10;
    }

    public const int CounterDamagePercent = 125;

    // SF4's low-life reduction, by the defender's health before the hit: under 50% x0.95, under 30%
    // x0.90, under 15% x0.75.
    public static int LowLifePercent(int health, int maxHealth) =>
        health * 100 < maxHealth * 15 ? 75
        : health * 100 < maxHealth * 30 ? 90
        : health * 100 < maxHealth * 50 ? 95
        : 100;

    // A hit's damage: the move's, +25% on a counter hit (only ever the first hit of a combo: a
    // fighter in hitstun can't be in an attack's startup), scaled by its place in the combo, reduced
    // at low life if that's on, and never less than 1. Rounded down at each step.
    public static int Damage(int baseDamage, int hitInCombo, bool counter, int health, int maxHealth, bool lowLifeReduction)
    {
        var damage = baseDamage;
        if (counter)
            damage = damage * CounterDamagePercent / 100;
        damage = damage * ScalingPercent(hitInCombo) / 100;
        if (lowLifeReduction)
            damage = damage * LowLifePercent(health, maxHealth) / 100;
        return damage > 1 ? damage : 1;
    }

    // Stun scales the same way as damage, but with no low-life reduction and no minimum.
    public static int Stun(int baseStun, int hitInCombo, bool counter)
    {
        var stun = counter ? baseStun * CounterDamagePercent / 100 : baseStun;
        return stun * ScalingPercent(hitInCombo) / 100;
    }

    // Hit in the startup of its own attack (before the attack's first active frame).
    public static bool InStartup(in FighterState fighter, Character character) =>
        fighter.Action == (int)FighterAction.Attack && fighter.Move >= 0
        && fighter.ActionFrame < character.Moves[fighter.Move].FirstActiveFrame;

    // Extra hitstun on a counter hit: +1 for lights, +3 for mediums and heavies.
    public static int CounterHitstun(Strength strength) => strength == Strength.Light ? 1 : 3;

    // Crouching for hitstun: down in a crouch, a crouching guard, the crouch transition, or a
    // crouching normal.
    public static bool Crouching(in FighterState fighter, Character character) =>
        fighter.Action is (int)FighterAction.Crouch or (int)FighterAction.CrouchDown or (int)FighterAction.BlockCrouch
        || (fighter.Action == (int)FighterAction.Attack && fighter.Move >= 0 && character.Moves[fighter.Move].Crouching);

    // A falling (juggled) fighter can only be hit by a move whose juggle limit reaches its count.
    public static bool CanHit(Move move, in FighterState defender) =>
        !(defender.Airborne && defender.Juggle > 0) || move.JuggleLimit >= defender.Juggle;
}
