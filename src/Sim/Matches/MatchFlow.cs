namespace StickFighter.Sim.Matches;

// The match around the fighting: intro, fight, round end, next round or match end. Step asks
// whether fighters may act and whether hits land this frame, runs the fight, then calls Advance,
// so the whole match goes through the step and replays, rolls back and SyncTests like the rest.
public static class MatchFlow
{
    // "Round N" for a second, then "Fight!" for half a second; the round starts on the next frame.
    public const int IntroFrames = 90;

    // The KO or time-over call before the next round or the result.
    public const int RoundEndFrames = 120;

    public const int RoundSeconds = 99;
    public const int RoundFrames = RoundSeconds * GameInfo.TicksPerSecond;

    public const int WinsToMatch = 2;

    public static MatchState Start(MatchMode mode) => mode == MatchMode.Versus
        ? new MatchState { Mode = mode, Phase = MatchPhase.Intro, Round = 1, TimerFrames = RoundFrames, Winner = MatchWinner.None }
        : new MatchState { Mode = mode, Phase = MatchPhase.Fight, Winner = MatchWinner.None };

    // Fighters act and attacks land only while fighting. Outside it, inputs are still recorded, so
    // a press in the last frames of the intro is buffered and comes out on the first fighting frame.
    public static bool Fighting(in MatchState match) => match.Phase == MatchPhase.Fight;

    // After the frame's fighting: the timer, KO and time over, and moving between phases; in
    // training, refilling health.
    public static void Advance(ref GameState state)
    {
        ref var match = ref state.Match;
        if (match.Mode == MatchMode.Training)
            RefillHealth(ref state);
        if (match.Mode != MatchMode.Versus)
            return;

        match.PhaseFrame++;
        switch (match.Phase)
        {
            case MatchPhase.Intro:
                if (match.PhaseFrame >= IntroFrames)
                    Enter(ref match, MatchPhase.Fight);
                break;

            case MatchPhase.Fight:
                if (match.TimerFrames > 0)
                    match.TimerFrames--;
                var result = Result(state);
                if (result != RoundResult.None)
                {
                    match.LastResult = result;
                    Enter(ref match, MatchPhase.RoundEnd);
                }
                break;

            case MatchPhase.RoundEnd:
                if (match.PhaseFrame >= RoundEndFrames)
                    EndRound(ref state);
                break;
        }
    }

    // A KO, a double KO, time over (most health wins), or still going.
    public static RoundResult Result(in GameState state)
    {
        var p1 = state.Fighters[0].Health;
        var p2 = state.Fighters[1].Health;
        if (p1 == 0 || p2 == 0)
            return p1 == 0 && p2 == 0 ? RoundResult.DoubleKo : p1 == 0 ? RoundResult.P2 : RoundResult.P1;
        if (state.Match.TimerFrames > 0)
            return RoundResult.None;
        return p1 > p2 ? RoundResult.P1 : p2 > p1 ? RoundResult.P2 : RoundResult.Draw;
    }

    // Scores the round, then either decides the match or sets up the next round from the start
    // positions with health and everything else reset.
    private static void EndRound(ref GameState state)
    {
        ref var match = ref state.Match;
        var result = match.LastResult;
        var tied = result is RoundResult.DoubleKo or RoundResult.Draw;

        if (match.FinalRound)
        {
            match.Winner = tied ? MatchWinner.Draw : result == RoundResult.P1 ? MatchWinner.P1 : MatchWinner.P2;
            Enter(ref match, MatchPhase.MatchEnd);
            return;
        }

        // A double KO or a draw counts for both players, as in SF4.
        if (result == RoundResult.P1 || tied)
            match.P1Wins++;
        if (result == RoundResult.P2 || tied)
            match.P2Wins++;

        var p1Through = match.P1Wins >= WinsToMatch;
        var p2Through = match.P2Wins >= WinsToMatch;
        if (p1Through != p2Through)
        {
            match.Winner = p1Through ? MatchWinner.P1 : MatchWinner.P2;
            Enter(ref match, MatchPhase.MatchEnd);
            return;
        }

        // Both reached match point together: one more round decides it.
        match.FinalRound = p1Through && p2Through;
        match.Round++;
        match.TimerFrames = RoundFrames;
        match.LastResult = RoundResult.None;
        GameState.PlaceFighters(ref state);
        Enter(ref match, MatchPhase.Intro);
    }

    // SF4's training room: health stays down while a combo is going, so its damage reads on the
    // bar, then refills once the fighter is free again (out of hitstun, a juggle, a throw and a
    // knockdown).
    private static void RefillHealth(ref GameState state)
    {
        var full = Characters.Characters.Placeholder.Stats.Health;
        for (var p = 0; p < GameState.PlayerCount; p++)
        {
            ref var fighter = ref state.Fighters[p];
            var reeling = fighter.Hitstun > 0 || (fighter.Airborne && fighter.Juggle > 0) || fighter.Grab >= 0 || fighter.KnockedDown;
            if (!reeling)
                fighter.Health = full;
        }
    }

    private static void Enter(ref MatchState match, MatchPhase phase)
    {
        match.Phase = phase;
        match.PhaseFrame = 0;
    }
}
