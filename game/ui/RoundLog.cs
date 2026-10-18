using System.Collections.Generic;
using System.Linq;
using StickFighter.Sim;

namespace StickFighter.UI;

// One round's outcome, for the results screen: who took it (None for a double KO or draw) and how.
public sealed record RoundOutcome(int Round, MatchWinner Winner, Announcement How);

// A finished match, for the results screen.
public sealed record MatchResults(MatchWinner Winner, int P1Wins, int P2Wins, IReadOnlyList<RoundOutcome> Rounds);

// The rounds as they went. The match state keeps only the score, so this notes each round's
// outcome as its round end is presented, by round number: a rollback that presents a round end
// again overwrites the same entry.
public sealed class RoundLog
{
    private readonly SortedDictionary<int, RoundOutcome> _rounds = new();

    public void Present(in GameState state)
    {
        var match = state.Match;
        if (match.Phase is not (MatchPhase.RoundEnd or MatchPhase.MatchEnd) || match.LastResult == RoundResult.None)
            return;
        var (kind, perfect) = MatchHud.Call(state);
        var winner = match.LastResult switch
        {
            RoundResult.P1 => MatchWinner.P1,
            RoundResult.P2 => MatchWinner.P2,
            _ => MatchWinner.None,
        };
        _rounds[match.Round] = new RoundOutcome(match.Round, winner, perfect ? Announcement.Perfect : kind);
    }

    public MatchResults Results(in GameState state) =>
        new(state.Match.Winner, state.Match.P1Wins, state.Match.P2Wins, _rounds.Values.ToArray());
}
