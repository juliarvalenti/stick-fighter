using System.Collections.Generic;
using System.IO;
using System.Linq;
using StickFighter.Sim;
using StickFighter.Sim.Replays;
using StickFighter.Sound;
using StickFighter.UI;
using Xunit;

namespace StickFighter.Presentation.Tests;

// The announcer: each call voiced once whatever rollback does, the right line for each call, one
// line at a time, and a file for every line.
public class VoiceTests
{
    private sealed class FakeSink : IVoiceSink
    {
        public readonly List<VoiceLine> Played = new();
        public int Stops;

        public float Play(VoiceLine line, float gainDb)
        {
            Played.Add(line);
            return line == VoiceLine.FinalRound ? 1.5f : 0.7f;
        }

        public void Stop() => Stops++;
    }

    // The double-K.O. best-of-three (both trade to a double K.O., then P2 wins untouched), played
    // to the end of the match with a rollback of 1 to 8 frames on every frame, every frame it passes
    // through presented, as an online session would.
    [Fact]
    public void UnderRollbackEachCallIsVoicedOnce()
    {
        var replay = Replay.Read(File.ReadAllText(Path.Combine(RepoRoot(), "replays", "match-double-ko.sfr")));
        FrameInput Input(int f) => f < replay.Frames ? replay.Inputs[f] : default;

        var states = new List<GameState> { GameState.NewMatch(replay.Seed, replay.Stage, replay.Mode) };
        var calls = new VoiceCalls();
        var said = new List<VoiceLine>();
        void Present(in GameState s)
        {
            if (calls.Present(s) is { } line)
                said.Add(line);
        }

        for (var f = 0; states[^1].Match.Phase != MatchPhase.MatchEnd || states[^1].Match.PhaseFrame < 30; f++)
        {
            var next = states[f];
            Simulation.Step(ref next, Input(f));
            states.Add(next);
            Present(next);

            var back = System.Math.Min(1 + f % 8, f + 1);
            var again = states[f + 1 - back];
            for (var g = f + 1 - back; g <= f; g++)
            {
                Simulation.Step(ref again, Input(g));
                Present(again);
            }
            Assert.True(f < replay.Frames + 1000, "the match should end");
        }

        Assert.Equal(
            new[]
            {
                VoiceLine.Round1, VoiceLine.Fight, VoiceLine.DoubleKO,
                VoiceLine.Round2, VoiceLine.Fight, VoiceLine.KO, VoiceLine.Perfect,
                VoiceLine.P2Wins,
            },
            said);
    }

    [Fact]
    public void EveryCallHasItsLine()
    {
        Assert.Equal(VoiceLine.Round1, VoiceLines.For(Announcement.Round, 1, finalRound: false));
        Assert.Equal(VoiceLine.Round2, VoiceLines.For(Announcement.Round, 2, finalRound: false));
        Assert.Equal(VoiceLine.Round3, VoiceLines.For(Announcement.Round, 3, finalRound: false));
        Assert.Equal(VoiceLine.FinalRound, VoiceLines.For(Announcement.Round, 3, finalRound: true));
        Assert.Equal(VoiceLine.FinalRound, VoiceLines.For(Announcement.Round, 4, finalRound: true));
        Assert.Equal(VoiceLine.Fight, VoiceLines.For(Announcement.Fight, 2, finalRound: false));
        Assert.Equal(VoiceLine.KO, VoiceLines.For(Announcement.KO, 1, finalRound: false));
        Assert.Equal(VoiceLine.Time, VoiceLines.For(Announcement.Time, 1, finalRound: false));
        Assert.Equal(VoiceLine.Perfect, VoiceLines.For(Announcement.Perfect, 1, finalRound: false));
        Assert.Equal(VoiceLine.DoubleKO, VoiceLines.For(Announcement.DoubleKO, 1, finalRound: false));
        Assert.Equal(VoiceLine.P1Wins, VoiceLines.ForResult(MatchWinner.P1));
        Assert.Equal(VoiceLine.P2Wins, VoiceLines.ForResult(MatchWinner.P2));
        Assert.Equal(VoiceLine.Draw, VoiceLines.ForResult(MatchWinner.Draw));
        Assert.Null(VoiceLines.ForResult(MatchWinner.None));
    }

    [Fact]
    public void EveryLineHasItsFileAndSource()
    {
        var dir = Path.Combine(RepoRoot(), "assets", "audio", "announcer");
        var source = File.ReadAllText(Path.Combine(dir, "SOURCE.md"));
        foreach (var line in System.Enum.GetValues<VoiceLine>())
        {
            var file = VoiceLines.File(line);
            Assert.True(File.Exists(Path.Combine(dir, file + ".mp3")), $"{file}.mp3 is missing");
            Assert.True(File.Exists(Path.Combine(dir, "raw", file + ".mp3")), $"raw/{file}.mp3 is missing");
            Assert.Contains($"`{file}.mp3`", source);
        }
    }

    // "Final round" outlasts the round call, so "Fight!" waits for it, though never more than half a
    // second, and the music is ducked all the while.
    [Fact]
    public void ALineWaitsForTheOneBeforeButNotForLong()
    {
        var sink = new FakeSink();
        var voice = new VoiceDirector(sink);
        voice.Call(VoiceLine.FinalRound, 0); // 1.5 s: 90 frames
        voice.Call(VoiceLine.Fight, 50);
        for (var f = 50; f < VoiceDirector.MaxWaitFrames + 50; f++)
        {
            voice.Update(f);
            Assert.True(voice.Speaking(f));
        }
        Assert.Equal(new[] { VoiceLine.FinalRound }, sink.Played);

        voice.Update(50 + VoiceDirector.MaxWaitFrames); // frame 80, before "Final round" ends at 90
        Assert.Equal(new[] { VoiceLine.FinalRound, VoiceLine.Fight }, sink.Played);
        Assert.Equal(1, sink.Stops); // the rest of "Final round" gave way

        // A call after the line has finished starts at once, and the voice falls quiet after it.
        voice.Call(VoiceLine.KO, 200);
        Assert.Equal(VoiceLine.KO, sink.Played[^1]);
        Assert.True(voice.Speaking(200));
        Assert.False(voice.Speaking(200 + 42 + 1));
    }

    [Fact]
    public void TheMusicDucksUnderTheVoiceAndMoreUnderPause()
    {
        Assert.Equal(MusicDirector.VoiceDuckDb, MusicDirector.GainDb(100, paused: false, voice: true), 3);
        Assert.Equal(MusicDirector.PausedDuckDb, MusicDirector.GainDb(100, paused: true, voice: true), 3);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(System.AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "StickFighter.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("repo root");
    }
}
