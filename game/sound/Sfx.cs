using StickFighter.Sim;
using StickFighter.Sim.Characters;

namespace StickFighter.Sound;

// The combat sounds.
public enum Sfx
{
    Whiff,
    HitLight,
    HitMedium,
    HitHeavy,
    CounterHit,
    Block,
    Throw,
    Knockdown,
    Ko,
    Clash,
}

// Which sound each of the sim's events makes, and how loud it sits in the mix.
public static class Cues
{
    public static Sfx? For(in SimEvent e) => e.Kind switch
    {
        EventKind.Hit => Strength(e) switch
        {
            Sim.Characters.Strength.Light => Sfx.HitLight,
            Sim.Characters.Strength.Medium => Sfx.HitMedium,
            _ => Sfx.HitHeavy,
        },
        EventKind.CounterHit => Sfx.CounterHit,
        EventKind.Block => Sfx.Block,
        EventKind.Throw => Sfx.Throw,
        EventKind.Knockdown => Sfx.Knockdown,
        EventKind.Ko => Sfx.Ko,
        EventKind.Clash => Sfx.Clash,
        _ => null,
    };

    // Relative levels, in dB: the KO on top, whiffs and blocks under the hits.
    public static float LevelDb(Sfx sfx) => sfx switch
    {
        Sfx.Whiff => -8,
        Sfx.Block => -5,
        Sfx.HitLight => -4,
        Sfx.HitMedium => -2,
        Sfx.Knockdown => -3,
        Sfx.Clash => -4,
        Sfx.Ko => 0,
        _ => -1,
    };

    private static Strength Strength(in SimEvent e) =>
        e.Move >= 0 ? Characters.Placeholder.Moves[e.Move].Strength : Sim.Characters.Strength.Heavy;
}
