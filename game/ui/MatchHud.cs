using StickFighter.Sim;
using StickFighter.Sim.Matches;

namespace StickFighter.UI;

// Fills the HUD from the match. Everything but the damage trail is a pure function of the state,
// and the trail is a pure function of the states presented, by frame: a frame's HUD looks the same
// however it was reached, rollback included. No Godot here, so it's tested without the engine.
public static class MatchHud
{
    // The intro's 90 frames: "ROUND N" for the first second, then "FIGHT!", which carries on half a
    // second into the fight.
    public const int RoundCallFrames = 60;
    public const int FightCallFrames = 60;
    private const int FightLead = MatchFlow.IntroFrames - RoundCallFrames;

    // At round end: the call (K.O., TIME, DOUBLE K.O.) for a second, then PERFECT for the rest of
    // the round end if the winner took no damage; without a perfect, the call holds throughout.
    public const int CallFrames = 60;

    // How long the last round's call stays up before the results screen comes in.
    public const int ResultsDelay = 60;

    public static void Fill(HudModel model, in GameState state, HealthTrail trail)
    {
        var character = Sim.Characters.Characters.Placeholder;
        var match = state.Match;
        var versus = match.Mode == MatchMode.Versus;

        model.Timer = versus ? match.TimerSeconds : null;
        model.RoundsToWin = versus ? MatchFlow.WinsToMatch : 0;
        model.Combo = null; // combos are counted from Hit states on
        model.Announcement = Announcement(state);

        for (var p = 0; p < GameState.PlayerCount; p++)
        {
            var player = model.Players[p];
            var fighter = state.Fighters[p];
            player.MaxHealth = character.Stats.Health;
            player.Health = fighter.Health;
            player.RecentHealth = trail.At(p, state.Frame, fighter.Health);
            player.MaxStun = character.Stats.Stun;
            player.Stun = 0; // stun builds from Hit states on
            player.RoundWins = p == 0 ? match.P1Wins : match.P2Wins;
            player.Meter = null; // Meter and supers
            player.Callout = null; // COUNTER needs counter hits, from Hit states on
        }
    }

    // The announcement on screen this frame, timed from the match phase, so its age is the same
    // whenever this frame is drawn.
    public static HudAnnouncement? Announcement(in GameState state)
    {
        var match = state.Match;
        if (match.Mode != MatchMode.Versus)
            return null;

        switch (match.Phase)
        {
            case MatchPhase.Intro:
                return match.PhaseFrame < RoundCallFrames
                    ? new HudAnnouncement(UI.Announcement.Round, match.PhaseFrame, match.Round, RoundCallFrames)
                    : new HudAnnouncement(UI.Announcement.Fight, match.PhaseFrame - RoundCallFrames, match.Round, FightCallFrames);

            case MatchPhase.Fight:
                var age = FightLead + match.PhaseFrame;
                return age < FightCallFrames ? new HudAnnouncement(UI.Announcement.Fight, age, match.Round, FightCallFrames) : null;

            case MatchPhase.RoundEnd:
                var call = Call(state);
                if (!call.Perfect)
                    return new HudAnnouncement(call.Kind, match.PhaseFrame, match.Round, MatchFlow.RoundEndFrames);
                return match.PhaseFrame < CallFrames
                    ? new HudAnnouncement(call.Kind, match.PhaseFrame, match.Round, CallFrames)
                    : new HudAnnouncement(UI.Announcement.Perfect, match.PhaseFrame - CallFrames, match.Round, MatchFlow.RoundEndFrames - CallFrames);

            default:
                return null;
        }
    }

    // How the round in its round end (or the last round, at match end) was decided: K.O., TIME
    // (a time-over win or a draw) or DOUBLE K.O., and whether the winner took no damage.
    public static (Announcement Kind, bool Perfect) Call(in GameState state)
    {
        var result = state.Match.LastResult;
        if (result == RoundResult.DoubleKo)
            return (UI.Announcement.DoubleKO, false);
        if (result is not (RoundResult.P1 or RoundResult.P2))
            return (UI.Announcement.Time, false);

        var winner = result == RoundResult.P1 ? 0 : 1;
        var ko = state.Fighters[1 - winner].Health == 0;
        var perfect = state.Fighters[winner].Health == Sim.Characters.Characters.Placeholder.Stats.Health;
        return (ko ? UI.Announcement.KO : UI.Announcement.Time, perfect);
    }
}
