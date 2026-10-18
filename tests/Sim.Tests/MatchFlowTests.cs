using StickFighter.Sim.Characters;
using StickFighter.Sim.Matches;
using StickFighter.Sim.Numerics;
using StickFighter.Sim.Replays;
using Xunit;

namespace StickFighter.Sim.Tests;

public class MatchFlowTests
{
    private static Character Data => Characters.Characters.Placeholder;

    private static GameState Versus() => GameState.NewMatch(1, Stage.Training, MatchMode.Versus);

    private static void Run(ref GameState state, int frames, FrameInput input = default)
    {
        for (var i = 0; i < frames; i++)
            Simulation.Step(ref state, input);
    }

    // Through the intro: the next step is the round's first fighting frame.
    private static GameState Fighting()
    {
        var state = Versus();
        Run(ref state, MatchFlow.IntroFrames);
        Assert.Equal(MatchPhase.Fight, state.Match.Phase);
        return state;
    }

    // Close enough for a jab to land, as in NormalsTests.
    private static void Close(ref GameState state)
    {
        state.Fighters[0].Position = new FixVec2(Fix.FromInt(600), state.Fighters[0].Position.Y);
        state.Fighters[1].Position = new FixVec2(Fix.FromInt(660), state.Fighters[1].Position.Y);
    }

    // Ends the round in progress with the given healths, then plays out the round-end call.
    private static void EndRound(ref GameState state, int p1Health, int p2Health)
    {
        state.Fighters[0].Health = p1Health;
        state.Fighters[1].Health = p2Health;
        Simulation.Step(ref state, default);
        Assert.Equal(MatchPhase.RoundEnd, state.Match.Phase);
        Run(ref state, MatchFlow.RoundEndFrames);
    }

    private static void WinRound(ref GameState state, int player)
    {
        EndRound(ref state, player == 0 ? 500 : 0, player == 1 ? 500 : 0);
        if (state.Match.Phase == MatchPhase.Intro)
            Run(ref state, MatchFlow.IntroFrames);
    }

    [Fact]
    public void TrainingHasNoIntroTimerOrRounds()
    {
        var state = GameState.NewMatch(1);
        Assert.Equal(MatchMode.Training, state.Match.Mode);
        Assert.True(MatchFlow.Fighting(state.Match));

        state.Fighters[1].Health = 0;
        Run(ref state, 600);
        Assert.Equal(MatchPhase.Fight, state.Match.Phase);
        Assert.Equal(0, state.Match.TimerFrames);
        Assert.Equal(0, state.Match.Round);
    }

    [Fact]
    public void VersusStartsWithRoundOneIntroAndAFullTimer()
    {
        var state = Versus();
        Assert.Equal(MatchPhase.Intro, state.Match.Phase);
        Assert.Equal(1, state.Match.Round);
        Assert.Equal(MatchFlow.RoundFrames, state.Match.TimerFrames);
        Assert.Equal(99, state.Match.TimerSeconds);
        Assert.Equal(MatchWinner.None, state.Match.Winner);
    }

    [Fact]
    public void NobodyMovesOrAttacksInTheIntro()
    {
        var state = Versus();
        var start = state.Fighters[0].Position;
        Run(ref state, MatchFlow.IntroFrames - 10, new FrameInput(Buttons.Right, Buttons.Left));
        Run(ref state, 5, new FrameInput(Buttons.HeavyKick | Buttons.Up, Buttons.LightPunch | Buttons.LightKick));

        Assert.Equal(MatchPhase.Intro, state.Match.Phase);
        for (var p = 0; p < GameState.PlayerCount; p++)
        {
            Assert.Equal((int)FighterAction.Stand, state.Fighters[p].Action);
            Assert.False(state.Fighters[p].Airborne);
        }
        Assert.Equal(start, state.Fighters[0].Position);
        Assert.Equal(MatchFlow.RoundFrames, state.Match.TimerFrames);
    }

    // The countdown rule: inputs are recorded through the intro, so a press just before "Fight!"
    // is buffered and comes out on the first fighting frame.
    [Fact]
    public void APressAtTheEndOfTheIntroComesOutOnTheFirstFightingFrame()
    {
        var state = Versus();
        Run(ref state, MatchFlow.IntroFrames - 2);
        Simulation.Step(ref state, new FrameInput(Buttons.MediumPunch, default));
        Simulation.Step(ref state, default);
        Assert.Equal(MatchPhase.Fight, state.Match.Phase);
        Assert.Equal((int)FighterAction.Stand, state.Fighters[0].Action);

        Simulation.Step(ref state, default);
        Assert.Equal((int)FighterAction.Attack, state.Fighters[0].Action);
        Assert.Equal("5MP", Data.Moves[state.Fighters[0].Move].Input);
        Assert.Equal(0, state.Fighters[0].ActionFrame);
    }

    [Fact]
    public void ThePressMustStillBeInTheBuffer()
    {
        var state = Versus();
        Run(ref state, MatchFlow.IntroFrames - 30);
        Simulation.Step(ref state, new FrameInput(Buttons.MediumPunch, default));
        Run(ref state, 30);
        Assert.Equal((int)FighterAction.Stand, state.Fighters[0].Action);
    }

    [Fact]
    public void TheTimerRunsOnlyWhileFightingAndShowsWholeSecondsRoundedUp()
    {
        var state = Fighting();
        Assert.Equal(MatchFlow.RoundFrames, state.Match.TimerFrames);

        Simulation.Step(ref state, default);
        Assert.Equal(MatchFlow.RoundFrames - 1, state.Match.TimerFrames);
        Assert.Equal(99, state.Match.TimerSeconds);

        Run(ref state, GameInfo.TicksPerSecond - 1);
        Assert.Equal(98, state.Match.TimerSeconds);

        state.Fighters[1].Health = 0;
        Simulation.Step(ref state, default);
        var stopped = state.Match.TimerFrames;
        Run(ref state, MatchFlow.RoundEndFrames - 1);
        Assert.Equal(MatchPhase.RoundEnd, state.Match.Phase);
        Assert.Equal(stopped, state.Match.TimerFrames);
    }

    [Fact]
    public void AJabThatEmptiesTheHealthBarIsAKo()
    {
        var state = Fighting();
        Close(ref state);
        state.Fighters[1].Health = 1;
        Simulation.Step(ref state, new FrameInput(Buttons.LightPunch, default));
        Run(ref state, Data.FindMove("5LP")!.TotalFrames);

        Assert.Equal(0, state.Fighters[1].Health);
        Assert.Equal(MatchPhase.RoundEnd, state.Match.Phase);
        Assert.Equal(RoundResult.P1, state.Match.LastResult);
    }

    [Fact]
    public void ATradeThatEmptiesBothBarsIsADoubleKo()
    {
        var state = Fighting();
        Close(ref state);
        state.Fighters[0].Health = 1;
        state.Fighters[1].Health = 1;
        Simulation.Step(ref state, new FrameInput(Buttons.LightPunch, Buttons.LightPunch));
        Run(ref state, 6);

        Assert.Equal(MatchPhase.RoundEnd, state.Match.Phase);
        Assert.Equal(RoundResult.DoubleKo, state.Match.LastResult);
    }

    [Theory]
    [InlineData(600, 400, RoundResult.P1)]
    [InlineData(400, 600, RoundResult.P2)]
    [InlineData(500, 500, RoundResult.Draw)]
    public void AtTimeOverMostHealthWins(int p1, int p2, RoundResult expected)
    {
        var state = Fighting();
        state.Fighters[0].Health = p1;
        state.Fighters[1].Health = p2;
        Run(ref state, MatchFlow.RoundFrames - 1);
        Assert.Equal(MatchPhase.Fight, state.Match.Phase);
        Assert.Equal(1, state.Match.TimerSeconds);

        Simulation.Step(ref state, default);
        Assert.Equal(0, state.Match.TimerFrames);
        Assert.Equal(MatchPhase.RoundEnd, state.Match.Phase);
        Assert.Equal(expected, state.Match.LastResult);
    }

    // After a KO nothing lands: a move already started plays out but can't hit, nothing new
    // starts, and the loser can't be hit again or hit back.
    [Fact]
    public void NothingLandsAfterTheRoundEnds()
    {
        var state = Fighting();
        Close(ref state);
        state.Fighters[0].Health = 0;
        Simulation.Step(ref state, new FrameInput(Buttons.LightPunch, Buttons.LightPunch));
        Assert.Equal(MatchPhase.RoundEnd, state.Match.Phase);
        Assert.Equal((int)FighterAction.Attack, state.Fighters[1].Action);

        Run(ref state, 20, new FrameInput(Buttons.LightPunch, Buttons.LightPunch | Buttons.LightKick));
        Assert.Equal(0, state.Fighters[0].Health);
        Assert.Equal(Data.Stats.Health, state.Fighters[1].Health);
        Assert.Equal((int)FighterAction.Stand, state.Fighters[0].Action);
        Assert.Equal((int)FighterAction.Stand, state.Fighters[1].Action);
    }

    [Fact]
    public void TheNextRoundStartsFromTheStartPositionsWithEverythingReset()
    {
        var state = Fighting();
        var fresh = Versus();
        Run(ref state, 40, new FrameInput(Buttons.Right, Buttons.Right));
        state.Fighters[0].Hitstun = 5;
        state.Fighters[1].Health = 0;
        Simulation.Step(ref state, default);
        Run(ref state, MatchFlow.RoundEndFrames);

        Assert.Equal(MatchPhase.Intro, state.Match.Phase);
        Assert.Equal(2, state.Match.Round);
        Assert.Equal(MatchFlow.RoundFrames, state.Match.TimerFrames);
        Assert.Equal(RoundResult.None, state.Match.LastResult);
        Assert.Equal(1, state.Match.P1Wins);
        for (var p = 0; p < GameState.PlayerCount; p++)
            Assert.Equal(fresh.Fighters[p], state.Fighters[p]);
    }

    [Fact]
    public void TwoRoundsWinTheMatch()
    {
        var state = Fighting();
        WinRound(ref state, 1);
        WinRound(ref state, 0);
        Assert.Equal(3, state.Match.Round);
        WinRound(ref state, 1);

        Assert.Equal(MatchPhase.MatchEnd, state.Match.Phase);
        Assert.Equal(MatchWinner.P2, state.Match.Winner);
        Assert.Equal((1, 2), (state.Match.P1Wins, state.Match.P2Wins));

        // The match is over: nothing more happens.
        Run(ref state, 600, new FrameInput(Buttons.Right, Buttons.HeavyKick));
        Assert.Equal(MatchPhase.MatchEnd, state.Match.Phase);
        Assert.Equal(3, state.Match.Round);
    }

    [Fact]
    public void ADoubleKoOrDrawGivesBothPlayersTheRound()
    {
        var state = Fighting();
        EndRound(ref state, 0, 0);
        Assert.Equal((1, 1), (state.Match.P1Wins, state.Match.P2Wins));
        Assert.False(state.Match.FinalRound);
        Run(ref state, MatchFlow.IntroFrames);

        // A draw at time over: same.
        state.Fighters[0].Health = 300;
        state.Fighters[1].Health = 300;
        Run(ref state, MatchFlow.RoundFrames);
        Run(ref state, MatchFlow.RoundEndFrames);
        Assert.Equal((2, 2), (state.Match.P1Wins, state.Match.P2Wins));
        Assert.True(state.Match.FinalRound);
        Assert.Equal(MatchPhase.Intro, state.Match.Phase);
    }

    [Fact]
    public void ATieAtOneAllMeansAFinalRoundThatDecidesIt()
    {
        var state = Fighting();
        WinRound(ref state, 0);
        WinRound(ref state, 1);
        EndRound(ref state, 0, 0);
        Assert.True(state.Match.FinalRound);
        Assert.Equal(4, state.Match.Round);
        Run(ref state, MatchFlow.IntroFrames);

        WinRound(ref state, 1);
        Assert.Equal(MatchPhase.MatchEnd, state.Match.Phase);
        Assert.Equal(MatchWinner.P2, state.Match.Winner);
    }

    [Fact]
    public void ATiedFinalRoundIsADrawnMatch()
    {
        var state = Fighting();
        EndRound(ref state, 0, 0);
        Run(ref state, MatchFlow.IntroFrames);
        EndRound(ref state, 0, 0);
        Assert.True(state.Match.FinalRound);
        Run(ref state, MatchFlow.IntroFrames);

        EndRound(ref state, 0, 0);
        Assert.Equal(MatchPhase.MatchEnd, state.Match.Phase);
        Assert.Equal(MatchWinner.Draw, state.Match.Winner);
    }

    [Fact]
    public void AWinAtOneAllAfterATieWinsOutright()
    {
        // 1-0, then a double KO makes 2-1: P1 is through and P2 is not.
        var state = Fighting();
        WinRound(ref state, 0);
        EndRound(ref state, 0, 0);
        Assert.Equal(MatchPhase.MatchEnd, state.Match.Phase);
        Assert.Equal(MatchWinner.P1, state.Match.Winner);
    }

    // The versus replays show what their names say (a replay alone only pins checksums): each
    // round's result in order, "@time" for one decided at time over, then the winner and score.
    [Theory]
    [InlineData("match-ko-ko", "P1 P1", MatchWinner.P1, 2, 0)]
    [InlineData("match-time-over", "P1@time P1", MatchWinner.P1, 2, 0)]
    [InlineData("match-double-ko", "DoubleKo P2", MatchWinner.P2, 1, 2)]
    [InlineData("match-draw", "DoubleKo Draw@time DoubleKo", MatchWinner.Draw, 2, 2)]
    [InlineData("match-final-round", "P1 P2 DoubleKo P2", MatchWinner.P2, 2, 2)]
    public void VersusReplaysShowWhatTheySay(string name, string rounds, MatchWinner winner, int p1Wins, int p2Wins)
    {
        var replay = Replay.Read(System.IO.File.ReadAllText(System.IO.Path.Combine(RepoRoot(), "replays", name + ".sfr")));
        Assert.Equal(MatchMode.Versus, replay.Mode);
        var state = GameState.NewMatch(replay.Seed, replay.Stage, replay.Mode);
        var results = new System.Collections.Generic.List<string>();
        foreach (var input in replay.Inputs)
        {
            var before = state.Match.Phase;
            Simulation.Step(ref state, input);
            if (before == MatchPhase.Fight && state.Match.Phase == MatchPhase.RoundEnd)
                results.Add(state.Match.LastResult + (state.Match.TimerFrames == 0 ? "@time" : ""));
        }

        Assert.Equal(rounds, string.Join(" ", results));
        Assert.Equal(MatchPhase.MatchEnd, state.Match.Phase);
        Assert.Equal(winner, state.Match.Winner);
        Assert.Equal((p1Wins, p2Wins), (state.Match.P1Wins, state.Match.P2Wins));
    }

    private static string RepoRoot()
    {
        var dir = new System.IO.DirectoryInfo(System.AppContext.BaseDirectory);
        while (dir is not null && !System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "StickFighter.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new System.IO.DirectoryNotFoundException("repo root");
    }

    [Fact]
    public void TheMatchSurvivesRollbackAcrossARoundBoundary()
    {
        var inputs = new System.Collections.Generic.List<FrameInput>();
        Stepper koSoon = (ref GameState s, in FrameInput input) =>
        {
            // Empties P2's bar a few frames into each fight, so rounds end and restart under rollback.
            if (s.Match.Phase == MatchPhase.Fight && s.Match.PhaseFrame == 10)
                s.Fighters[1].Health = 0;
            Simulation.Step(ref s, input);
        };
        for (var i = 0; i < 2 * (MatchFlow.IntroFrames + 11 + MatchFlow.RoundEndFrames) + 20; i++)
            inputs.Add(new FrameInput(i % 2 == 0 ? Buttons.Right : Buttons.Left, i % 7 == 3 ? Buttons.LightPunch : Buttons.None));

        Assert.Null(Replays.SyncTest.Run(1, Stage.Training, inputs, koSoon, MatchMode.Versus));
        var state = Versus();
        foreach (var input in inputs)
            koSoon(ref state, input);
        Assert.Equal(MatchPhase.MatchEnd, state.Match.Phase);
        Assert.Equal(MatchWinner.P1, state.Match.Winner);
    }
}
