using System;
using System.Collections.Generic;
using System.Linq;
using StickFighter.Sound;
using Xunit;

namespace StickFighter.Presentation.Tests;

// The music: two short seamless loops, the same every run, and which one plays how loud, through a
// fake sink.
public class MusicTests
{
    private sealed class FakeSink : IMusicSink
    {
        public readonly List<(Track Track, float GainDb)> Played = new();
        public readonly List<float> Gains = new();

        public void Play(Track track, float gainDb) => Played.Add((track, gainDb));

        public void SetGain(float gainDb) => Gains.Add(gainDb);
    }

    [Fact]
    public void UnderAMinuteOfMaterialInAll()
    {
        var seconds = Enum.GetValues<Track>().Sum(t => MusicSynth.Generate(t).Length) / (float)MusicSynth.Rate;
        Assert.InRange(seconds, 20, 60);
    }

    [Fact]
    public void EachLoopIsCleanSeamlessAndTheSameEachTime()
    {
        foreach (var track in Enum.GetValues<Track>())
        {
            var loop = MusicSynth.Generate(track);
            Assert.Equal((int)(MusicSynth.Seconds(track) * MusicSynth.Rate), loop.Length);
            Assert.All(loop, s => Assert.InRange(s, -1f, 1f));
            Assert.True(loop.Max(Math.Abs) > 0.5f, $"{track} should be loud enough");

            // The end flows into the start: no bigger a step than anywhere inside the loop.
            var steps = loop.Zip(loop.Skip(1), (a, b) => Math.Abs(b - a)).ToList();
            Assert.True(Math.Abs(loop[0] - loop[^1]) <= steps.Max(), $"{track} should loop without a jump");
            Assert.Equal(loop, MusicSynth.Generate(track));
        }
    }

    [Fact]
    public void ATrackStartsOnceAndChangesWithTheScreen()
    {
        var sink = new FakeSink();
        var music = new MusicDirector(sink);
        for (var f = 0; f < 10; f++)
            music.Update(Track.Menu, 80, paused: false, f);
        for (var f = 10; f < 20; f++)
            music.Update(Track.Match, 80, paused: false, f);

        Assert.Equal(new[] { Track.Menu, Track.Match }, sink.Played.Select(p => p.Track));
        Assert.Empty(sink.Gains);
    }

    [Fact]
    public void TheMusicVolumeSetsTheLevelAndPauseDucksIt()
    {
        var sink = new FakeSink();
        var music = new MusicDirector(sink);
        music.Update(Track.Match, 100, paused: false, 0);
        music.Update(Track.Match, 100, paused: true, 1);
        music.Update(Track.Match, 50, paused: false, 2);
        music.Update(Track.Match, 0, paused: false, 3);

        Assert.Equal(0, sink.Played[0].GainDb, 3);
        Assert.Equal(MusicDirector.PausedDuckDb, sink.Gains[0], 3);
        Assert.True(sink.Gains[1] < -6, $"half volume should be well down: {sink.Gains[1]}");
        Assert.True(sink.Gains[2] <= SoundDirector.SilentDb, $"0 should be silent: {sink.Gains[2]}");
    }
}
