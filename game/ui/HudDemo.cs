namespace StickFighter.UI;

// The HUD with mock values for the `hud` and `style-hud` scenarios. The real match fills the
// model from game state; here each captured frame shows a different moment, so the goldens cover
// every element and every announcement over a real match frame.
public partial class HudDemo : Hud, IScenarioClock
{
    // Frames the `hud` scenario captures, and what each one shows.
    public static readonly int[] Captures = { 10, 20, 30, 41, 50, 60, 70, 80 };

    private readonly bool _static;

    // `still`: one fixed mid-round moment (the style frame); otherwise it follows the frame.
    public HudDemo(bool still = false)
    {
        _static = still;
        Model = MidRound();
    }

    public void At(int frame)
    {
        if (_static)
            return;
        Model = frame switch
        {
            < 15 => Opening(new HudAnnouncement(Announcement.Round, Age: 20)),
            < 25 => Opening(new HudAnnouncement(Announcement.Fight, Age: 6)), // mid-pop
            < 35 => Opening(new HudAnnouncement(Announcement.Fight, Age: 30)),
            < 45 => MidRound(),
            < 55 => Ending(Announcement.KO),
            < 65 => Ending(Announcement.Time),
            < 75 => Ending(Announcement.Perfect),
            _ => Ending(Announcement.DoubleKO),
        };
        Refresh();
    }

    // Full health, no meter yet, the round about to start.
    private static HudModel Opening(HudAnnouncement announcement)
    {
        var model = new HudModel { Timer = 99, Announcement = announcement };
        model.Players[0].RoundWins = 1;
        return model;
    }

    // Player 1 has taken some damage with the last hit still trailing, and is three hits into a
    // counter-hit combo; player 2 is low, stunned, with meter.
    private static HudModel MidRound()
    {
        var model = new HudModel { Timer = 58, Combo = new HudCombo(0, 3, 214) };
        var (p1, p2) = (model.Players[0], model.Players[1]);
        p1.Health = 720;
        p1.RecentHealth = 810;
        p1.Stun = 150;
        p1.RoundWins = 1;
        p1.Meter = 1.2;
        p1.Callout = "COUNTER";
        p2.Health = 220;
        p2.RecentHealth = 434;
        p2.Stun = 640;
        p2.Meter = 0.3;
        return model;
    }

    // A round over: P2 down to nothing, the announcement on screen.
    private static HudModel Ending(Announcement kind)
    {
        var model = MidRound();
        model.Combo = null;
        model.Players[0].Callout = null;
        model.Players[1].Health = 0;
        model.Players[1].RecentHealth = 0;
        model.Timer = kind == Announcement.Time ? 0 : 31;
        if (kind == Announcement.Perfect)
            model.Players[0].Health = model.Players[0].RecentHealth = 1000;
        if (kind == Announcement.DoubleKO)
            model.Players[0].Health = model.Players[0].RecentHealth = 0;
        model.Announcement = new HudAnnouncement(kind, Age: 30);
        return model;
    }
}
