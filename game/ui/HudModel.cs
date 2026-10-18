namespace StickFighter.UI;

// What the HUD shows, filled in by the match each frame. Plain values, no game state: the HUD reads
// this and nothing else, so it can't write back into the simulation.
public sealed class HudModel
{
    public HudPlayer[] Players { get; } = { new(), new() };

    // Seconds left in the round, or null for no limit (training).
    public int? Timer { get; set; } = 99;

    public int RoundsToWin { get; set; } = 2;

    // The combo on screen, if one is going, and whose it is.
    public HudCombo? Combo { get; set; }

    // The announcement playing, if any.
    public HudAnnouncement? Announcement { get; set; }
}

public sealed class HudPlayer
{
    public string Name { get; set; } = "STICK";
    public int Health { get; set; } = 1000;
    public int MaxHealth { get; set; } = 1000;

    // Health before the damage just taken: the trail that drains down to Health after a hit.
    public int RecentHealth { get; set; } = 1000;

    public int Stun { get; set; }
    public int MaxStun { get; set; } = 1000;
    public int RoundWins { get; set; }

    // Super meter, in stocks (0 to MeterStocks, fractional while filling), or null while the game
    // has no meter yet: the slot is drawn empty.
    public double? Meter { get; set; }
    public int MeterStocks { get; set; } = 2;

    // A callout under this player's side: "COUNTER", "REVERSAL"; null for none.
    public string? Callout { get; set; }
}

public sealed record HudCombo(int Player, int Hits, int Damage);

public enum Announcement
{
    Round,
    Fight,
    KO,
    Time,
    Perfect,
    DoubleKO,
}

// `Age` is frames since it started; it pops in, holds, and leaves over `Life` frames.
public sealed record HudAnnouncement(Announcement Kind, int Age, int Round = 1, int Life = 90);
