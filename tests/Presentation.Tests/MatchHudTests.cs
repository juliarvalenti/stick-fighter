using System.Collections.Generic;
using System.IO;
using System.Linq;
using StickFighter.Sim;
using StickFighter.Sim.Matches;
using StickFighter.Sim.Replays;
using StickFighter.UI;
using Xunit;

namespace StickFighter.Presentation.Tests;

public class MatchHudTests
{
    private static int FullHealth => Sim.Characters.Characters.Placeholder.Stats.Health;

    private static GameState Versus() => GameState.NewMatch(1, Stage.Training, MatchMode.Versus);

    private static void Run(ref GameState state, int frames)
    {
        for (var i = 0; i < frames; i++)
            Simulation.Step(ref state, default);
    }

    private static HudModel Filled(in GameState state)
    {
        var trail = new HealthTrail();
        trail.Record(state);
        var model = new HudModel();
        MatchHud.Fill(model, state, trail);
        return model;
    }

    [Fact]
    public void TrainingHasNoTimerPipsOrCalls()
    {
        var model = Filled(GameState.NewMatch(1));
        Assert.Null(model.Timer);
        Assert.Equal(0, model.RoundsToWin);
        Assert.Null(model.Announcement);
        Assert.Equal(FullHealth, model.Players[0].Health);
        Assert.Equal(FullHealth, model.Players[1].MaxHealth);
    }

    // "ROUND N" for the intro's first second, then "FIGHT!", which runs on into the fight.
    [Theory]
    [InlineData(0, Announcement.Round, 0)]
    [InlineData(59, Announcement.Round, 59)]
    [InlineData(60, Announcement.Fight, 0)]
    [InlineData(89, Announcement.Fight, 29)]
    [InlineData(90, Announcement.Fight, 30)] // the first fighting frame
    [InlineData(119, Announcement.Fight, 59)]
    public void TheIntroCallsTheRoundThenFight(int frames, Announcement kind, int age)
    {
        var state = Versus();
        Run(ref state, frames);
        var call = MatchHud.Announcement(state);
        Assert.NotNull(call);
        Assert.Equal((kind, age, 1), (call.Kind, call.Age, call.Round));
    }

    [Fact]
    public void TheCallsAreOverHalfASecondIntoTheFight()
    {
        var state = Versus();
        Run(ref state, MatchFlow.IntroFrames + 30);
        Assert.Equal(MatchPhase.Fight, state.Match.Phase);
        Assert.Null(MatchHud.Announcement(state));
    }

    [Fact]
    public void TheHudShowsTheTimerScoreAndHealth()
    {
        var state = Versus();
        Run(ref state, MatchFlow.IntroFrames + 61);
        state.Fighters[1].Health = 640;
        state.Match.P1Wins = 1;

        var model = Filled(state);
        Assert.Equal(98, model.Timer);
        Assert.Equal(MatchFlow.WinsToMatch, model.RoundsToWin);
        Assert.Equal((1, 0), (model.Players[0].RoundWins, model.Players[1].RoundWins));
        Assert.Equal(640, model.Players[1].Health);
        Assert.Null(model.Players[0].Meter);
    }

    // The combo and COUNTER are kept on the fighter taking them; the HUD shows them on the
    // attacker's side, the counter from the second hit.
    [Fact]
    public void AComboAndCounterShowOnTheAttackersSide()
    {
        var state = Versus();
        state.Fighters[1].ComboHits = 1;
        state.Fighters[1].ComboDamage = 60;
        state.Fighters[1].CounterHit = true;
        var model = Filled(state);
        Assert.Null(model.Combo);
        Assert.Equal("COUNTER", model.Players[0].Callout);
        Assert.Null(model.Players[1].Callout);

        state.Fighters[1].ComboHits = 3;
        state.Fighters[1].ComboDamage = 214;
        Assert.Equal(new HudCombo(0, 3, 214), Filled(state).Combo);

        // P1 taking one from P2: the counter on P2's side.
        state.Fighters[1] = default;
        state.Fighters[0].ComboHits = 2;
        state.Fighters[0].ComboDamage = 90;
        Assert.Equal(new HudCombo(1, 2, 90), Filled(state).Combo);

        // Recovered: the combo and the callout are gone, whatever the last hit was.
        state.Fighters[0].ComboHits = 0;
        state.Fighters[0].CounterHit = true;
        var after = Filled(state);
        Assert.Null(after.Combo);
        Assert.Null(after.Players[1].Callout);
    }

    [Fact]
    public void StunShowsAgainstTheCharactersStun()
    {
        var state = Versus();
        state.Fighters[1].Stun = 300;
        var model = Filled(state);
        Assert.Equal(300, model.Players[1].Stun);
        Assert.Equal(Sim.Characters.Characters.Placeholder.Stats.Stun, model.Players[1].MaxStun);
    }

    // Ends the round with these healths and steps `into` frames into the round end.
    private static GameState RoundEnd(int p1, int p2, int into, bool timeOver = false)
    {
        var state = Versus();
        Run(ref state, MatchFlow.IntroFrames);
        state.Fighters[0].Health = p1;
        state.Fighters[1].Health = p2;
        if (timeOver)
            state.Match.TimerFrames = 1;
        Run(ref state, 1 + into);
        Assert.Equal(MatchPhase.RoundEnd, state.Match.Phase);
        return state;
    }

    [Theory]
    [InlineData(700, 0, false, 0, Announcement.KO)]
    [InlineData(700, 0, false, 100, Announcement.KO)] // no perfect: the K.O. holds
    [InlineData(1000, 0, false, 30, Announcement.KO)]
    [InlineData(1000, 0, false, 70, Announcement.Perfect)] // untouched: PERFECT after the K.O.
    [InlineData(0, 0, false, 30, Announcement.DoubleKO)]
    [InlineData(400, 600, true, 30, Announcement.Time)]
    [InlineData(500, 500, true, 30, Announcement.Time)] // a draw
    public void TheRoundEndCallsHowItWasDecided(int p1, int p2, bool timeOver, int into, Announcement expected)
    {
        var state = RoundEnd(p1, p2, into, timeOver);
        Assert.Equal(expected, MatchHud.Announcement(state)!.Kind);
    }

    [Fact]
    public void TheTrailHoldsThenDrainsToTheHealthLeft()
    {
        var state = Versus();
        Run(ref state, MatchFlow.IntroFrames);
        var trail = new HealthTrail();
        trail.Record(state);
        state.Fighters[1].Health = 700;

        var trailAt = new List<int>();
        for (var i = 0; i < 80; i++)
        {
            Simulation.Step(ref state, default);
            trail.Record(state);
            trailAt.Add(trail.At(1, state.Frame, state.Fighters[1].Health));
        }

        Assert.Equal(FullHealth, trailAt[HealthTrail.HoldFrames - 1]); // held
        Assert.Equal(FullHealth - HealthTrail.DrainPerFrame, trailAt[HealthTrail.HoldFrames]); // draining
        Assert.Equal(700, trailAt[^1]); // drained down to the health left
        Assert.Equal(trailAt.OrderByDescending(h => h), trailAt); // never grows back
    }

    // Plays a replay with a rollback on every frame: back 1 to 8 frames, then forward again,
    // presenting every frame it passes through, as an online session would.
    private static (Announcer Announcer, RoundLog Log, int PresentedKoStarts, HudModel Hud) PlayWithRollback(Replay replay)
    {
        var states = new List<GameState> { GameState.NewMatch(replay.Seed, replay.Stage, replay.Mode) };
        var announcer = new Announcer();
        var log = new RoundLog();
        var trail = new HealthTrail();
        var koStarts = 0;

        void Present(in GameState s)
        {
            trail.Record(s);
            announcer.Present(s);
            log.Present(s);
            if (MatchHud.Announcement(s) is { Kind: Announcement.KO, Age: 0 })
                koStarts++;
        }

        for (var f = 0; f < replay.Frames; f++)
        {
            var next = states[f];
            Simulation.Step(ref next, replay.Inputs[f]);
            states.Add(next);
            Present(next);

            var back = System.Math.Min(1 + f % 8, f + 1);
            var again = states[f + 1 - back];
            for (var g = f + 1 - back; g <= f; g++)
            {
                Simulation.Step(ref again, replay.Inputs[g]);
                Present(again);
            }
            Assert.True(again.StateEquals(next));
        }

        var hud = new HudModel();
        MatchHud.Fill(hud, states[^1], trail);
        return (announcer, log, koStarts, hud);
    }

    private static (Announcer Announcer, RoundLog Log, HudModel Hud, GameState Final) Play(Replay replay)
    {
        var state = GameState.NewMatch(replay.Seed, replay.Stage, replay.Mode);
        var announcer = new Announcer();
        var log = new RoundLog();
        var trail = new HealthTrail();
        foreach (var input in replay.Inputs)
        {
            Simulation.Step(ref state, input);
            trail.Record(state);
            announcer.Present(state);
            log.Present(state);
        }
        var hud = new HudModel();
        MatchHud.Fill(hud, state, trail);
        return (announcer, log, hud, state);
    }

    // Rollback presents the frame a K.O. starts on again and again, but the K.O., and every other
    // call, starts once; the HUD and the round log come out as without rollback.
    [Fact]
    public void UnderRollbackEachAnnouncementStartsOnce()
    {
        var replay = ReadReplay("match-double-ko");
        var rolled = PlayWithRollback(replay);
        var straight = Play(replay);

        Assert.True(rolled.PresentedKoStarts > 1, "rollback presented the K.O.'s first frame more than once");
        Assert.Equal(
            new (int, Announcement)[]
            {
                (1, Announcement.Round), (1, Announcement.Fight), (1, Announcement.DoubleKO),
                (2, Announcement.Round), (2, Announcement.Fight), (2, Announcement.KO), (2, Announcement.Perfect),
            },
            rolled.Announcer.Started);
        Assert.Equal(straight.Announcer.Started, rolled.Announcer.Started);
        Assert.Equal(straight.Log.Results(straight.Final).Rounds, rolled.Log.Results(straight.Final).Rounds);
        for (var p = 0; p < GameState.PlayerCount; p++)
            Assert.Equal(straight.Hud.Players[p].RecentHealth, rolled.Hud.Players[p].RecentHealth);
    }

    [Theory]
    [InlineData("match-ko-ko", MatchWinner.P1, "P1 Perfect, P1 Perfect")]
    [InlineData("match-time-over", MatchWinner.P1, "P1 Perfect, P1 Perfect")]
    [InlineData("match-double-ko", MatchWinner.P2, "None DoubleKO, P2 Perfect")]
    [InlineData("match-draw", MatchWinner.Draw, "None DoubleKO, None Time, None DoubleKO")]
    [InlineData("match-final-round", MatchWinner.P2, "P1 Perfect, P2 Perfect, None DoubleKO, P2 Perfect")]
    public void TheResultsListEveryRoundAsItWent(string name, MatchWinner winner, string rounds)
    {
        var (_, log, _, final) = Play(ReadReplay(name));
        var results = log.Results(final);
        Assert.Equal(winner, results.Winner);
        Assert.Equal(rounds, string.Join(", ", results.Rounds.Select(r => $"{r.Winner} {r.How}")));
        Assert.Equal(Enumerable.Range(1, results.Rounds.Count), results.Rounds.Select(r => r.Round));
    }

    private static Replay ReadReplay(string name) =>
        Replay.Read(File.ReadAllText(Path.Combine(RepoRoot(), "replays", name + ".sfr")));

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(System.AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "StickFighter.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("repo root");
    }
}
