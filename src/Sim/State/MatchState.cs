namespace StickFighter.Sim;

// Sandbox: fighting from the first frame with no rounds, timer, KO or refills (the default, and what
// tests and scripted replays use). Versus: a best-of-three match. Training: training mode, as the
// menu starts it: the sandbox, plus health refilling once a combo is over.
public enum MatchMode
{
    Sandbox = 0,
    Versus = 1,
    Training = 2,
}

public enum MatchPhase
{
    // Fighting: fighters act, hits and throws land, and the round timer runs.
    Fight = 0,

    // "Round N", then "Fight!": inputs are recorded but fighters can't act yet.
    Intro = 1,

    // After a KO, double KO or time over: ongoing states play out, but nothing new starts and
    // nothing lands.
    RoundEnd = 2,

    // The match is decided. Fighters stand where they are.
    MatchEnd = 3,
}

public enum RoundResult
{
    None = 0,
    P1 = 1,
    P2 = 2,

    // Both reduced to 0 health on the same frame.
    DoubleKo = 3,

    // Time over with equal health.
    Draw = 4,
}

// Who won the match. The values are what's encoded, so they don't change.
public enum MatchWinner
{
    // The match is still on.
    None = -1,
    P1 = 0,
    P2 = 1,

    // A tied final round.
    Draw = 2,
}

// Where the match is: its phase and how long it's been in it, the round and its timer, and the
// score. Part of GameState, so it's checksummed, rolls back and replays like everything else.
public struct MatchState
{
    public const int EncodedSize = 4 * 9 + 1;

    public MatchMode Mode;
    public MatchPhase Phase;
    public int PhaseFrame;

    // 1, 2, 3...; the final round after a tie at match point is Round 4 or later.
    public int Round;

    // Frames left on the round timer (shown in whole seconds, rounded up).
    public int TimerFrames;

    public int P1Wins;
    public int P2Wins;

    // How the last round ended (shown during the round end).
    public RoundResult LastResult;

    public MatchWinner Winner;

    // This round decides the match: both players reached match point together.
    public bool FinalRound;

    public readonly int TimerSeconds => (TimerFrames + GameInfo.TicksPerSecond - 1) / GameInfo.TicksPerSecond;

    public readonly void Encode(ref StateWriter writer)
    {
        writer.Write((int)Mode);
        writer.Write((int)Phase);
        writer.Write(PhaseFrame);
        writer.Write(Round);
        writer.Write(TimerFrames);
        writer.Write(P1Wins);
        writer.Write(P2Wins);
        writer.Write((int)LastResult);
        writer.Write((int)Winner);
        writer.Write(FinalRound);
    }
}
