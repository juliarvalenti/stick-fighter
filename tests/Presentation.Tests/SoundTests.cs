using System;
using System.Collections.Generic;
using System.Linq;
using StickFighter.Sim;
using StickFighter.Sim.Numerics;
using StickFighter.Sound;
using Xunit;

namespace StickFighter.Presentation.Tests;

// Combat sound through a fake audio sink (CI has no audio device): each sound plays once however
// often a rollback resimulates its frame, a rolled-back hit's sound fades out, and the effects
// volume scales what's played.
public class SoundTests
{
    private sealed class FakeSink : IAudioSink
    {
        public readonly List<(int Voice, Sfx Sfx, float Pitch, float GainDb)> Played = new();
        public readonly List<int> Faded = new();

        public void Play(int voice, Sfx sfx, float pitch, float gainDb) => Played.Add((voice, sfx, pitch, gainDb));

        public void Fade(int voice) => Faded.Add(voice);
    }

    private static GameState Apart(int gap)
    {
        var state = GameState.NewMatch(1);
        state.Fighters[0].Position = new FixVec2(Fix.FromInt(1000), state.Fighters[0].Position.Y);
        state.Fighters[1].Position = new FixVec2(Fix.FromInt(1000 + gap), state.Fighters[1].Position.Y);
        return state;
    }

    private static Func<int, FrameInput> Press(Buttons buttons) => f => new FrameInput(f == 0 ? buttons : default, default);

    // A match as rollback runs it: each frame is simulated, then (with `rollback` > 0) the last
    // `rollback` frames are loaded and simulated again with the same inputs before the presentation
    // updates, as a late remote input that changed nothing would.
    private sealed class Run
    {
        public GameState State;
        public readonly FakeSink Sink = new();
        public readonly SoundDirector Sound;
        private readonly StateBuffer _saved = new(16);

        public Run(int gap)
        {
            State = Apart(gap);
            Sound = new SoundDirector(Sink) { Log = new List<string>() };
        }

        public void Play(Func<int, FrameInput> script, int frames, int rollback = 0)
        {
            for (var i = 0; i < frames; i++)
            {
                Step(script);
                if (rollback > 0 && State.Frame > rollback)
                {
                    var to = State.Frame - rollback;
                    Assert.True(_saved.TryLoad(to, ref State));
                    while (State.Frame < to + rollback)
                        Step(script);
                }
                Sound.Update(State);
            }
        }

        public void RollBack(int frames) => Assert.True(_saved.TryLoad(State.Frame - frames, ref State));

        private void Step(Func<int, FrameInput> script)
        {
            _saved.Save(State);
            Simulation.Step(ref State, script(State.Frame));
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public void AHitSoundsOnceUnderRollback(int rollback)
    {
        var run = new Run(60);
        run.Play(Press(Buttons.HeavyPunch), 40, rollback);

        var played = Assert.Single(run.Sink.Played);
        Assert.Equal(Sfx.HitHeavy, played.Sfx);
        Assert.Empty(run.Sink.Faded);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(8)]
    public void AWhiffSwishesOnceUnderRollback(int rollback)
    {
        var run = new Run(400);
        run.Play(Press(Buttons.LightPunch), 40, rollback);

        var played = Assert.Single(run.Sink.Played);
        Assert.Equal(Sfx.Whiff, played.Sfx);
        Assert.Empty(run.Sink.Faded);
    }

    // At 30 Hz (or catching up) the presentation sees every second or third frame, whichever one the
    // first active frame falls on: the swish still plays, once, keyed to that frame.
    [Theory]
    [InlineData(2, 0)]
    [InlineData(2, 1)]
    [InlineData(3, 0)]
    [InlineData(3, 1)]
    [InlineData(3, 2)]
    [InlineData(4, 3)]
    public void AWhiffIsCaughtWhenUpdatesSkipFrames(int every, int phase)
    {
        var expected = new Run(400);
        expected.Play(Press(Buttons.LightPunch), 30);

        foreach (var move in new[] { Buttons.LightPunch, Buttons.HeavyKick })
        {
            var state = Apart(400);
            var sink = new FakeSink();
            var sound = new SoundDirector(sink) { Log = new List<string>() };
            for (var f = 0; f < 60; f++)
            {
                Simulation.Step(ref state, Press(move)(f));
                if (f % every == phase)
                    sound.Update(state);
            }
            Assert.Equal(Sfx.Whiff, Assert.Single(sink.Played).Sfx);
            if (move == Buttons.LightPunch)
                Assert.Equal(Frame(expected.Sound.Log![0]), Frame(sound.Log[0]));
        }
    }

    // Two jabs in a row are two swishes, not one.
    [Fact]
    public void EachSwingSwishesOnItsOwn()
    {
        var run = new Run(400);
        run.Play(f => new FrameInput(f is 0 or 30 ? Buttons.LightPunch : default, default), 60);
        Assert.Equal(new[] { Sfx.Whiff, Sfx.Whiff }, run.Sink.Played.Select(p => p.Sfx));
    }

    // At point-blank the jab connects on its first active frame: a hit, no swish.
    [Fact]
    public void AnAttackThatConnectsDoesntSwish()
    {
        var run = new Run(60);
        run.Play(Press(Buttons.LightPunch), 30);
        Assert.Equal(new[] { Sfx.HitLight }, run.Sink.Played.Select(p => p.Sfx));
    }

    // The swish starts on the move's first active frame: the frame a hit would have landed on.
    [Fact]
    public void TheSwishIsOnTheFrameAHitWouldBe()
    {
        var hit = new Run(60);
        hit.Play(Press(Buttons.LightPunch), 30);
        var whiff = new Run(400);
        whiff.Play(Press(Buttons.LightPunch), 30);
        Assert.Equal(Frame(hit.Sound.Log![0]), Frame(whiff.Sound.Log![0]));
    }

    private static int Frame(string line) => int.Parse(line.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1]);

    // Roll back to before the press and resimulate without it: the hit never happened, so its
    // sound fades out.
    [Fact]
    public void ARolledBackHitIsCancelled()
    {
        var run = new Run(60);
        run.Play(Press(Buttons.HeavyPunch), 9);
        var played = Assert.Single(run.Sink.Played);

        run.RollBack(9);
        run.Play(_ => default, 12);

        Assert.Equal(played.Voice, Assert.Single(run.Sink.Faded));
        Assert.Contains(run.Sound.Log!, line => line.Contains("HitHeavy") && line.EndsWith("cancelled"));
    }

    [Fact]
    public void ARolledBackWhiffIsCancelled()
    {
        var run = new Run(400);
        run.Play(Press(Buttons.LightPunch), 6);
        Assert.Equal(Sfx.Whiff, Assert.Single(run.Sink.Played).Sfx);

        run.RollBack(6);
        run.Play(_ => default, 12);

        Assert.Single(run.Sink.Faded);
    }

    [Fact]
    public void TheEffectsVolumeScalesTheLevel()
    {
        float GainAt(int volume)
        {
            var run = new Run(60);
            run.Sound.EffectsVolume = volume;
            run.Play(Press(Buttons.HeavyPunch), 20);
            return Assert.Single(run.Sink.Played).GainDb;
        }

        var full = GainAt(100);
        var half = GainAt(50);
        var none = GainAt(0);
        Assert.Equal(Cues.LevelDb(Sfx.HitHeavy), full, 3);
        Assert.True(half < full - 6, $"half volume should be well down: {half} vs {full}");
        Assert.True(none <= SoundDirector.SilentDb, $"0 should be silent: {none}");
    }

    // More simultaneous sounds than voices: the oldest give way, and no more than Voices play.
    [Fact]
    public void ManySoundsAtOnceShareTheVoices()
    {
        var state = Apart(60);
        for (var f = 0; f < 10; f++)
            Simulation.Step(ref state, default);
        for (var i = 0; i < SimEventRing.MaxPerFrame; i++)
            state.Events[i] = new SimEvent { Frame = state.Frame - 1, Kind = EventKind.Hit, Attacker = i % 2, Defender = 1 - i % 2, Move = i };

        var sink = new FakeSink();
        new SoundDirector(sink).Update(state);

        Assert.Equal(SimEventRing.MaxPerFrame, sink.Played.Count);
        Assert.All(sink.Played, p => Assert.InRange(p.Voice, 0, SoundDirector.Voices - 1));
        Assert.Equal(SimEventRing.MaxPerFrame - SoundDirector.Voices, sink.Faded.Count);
    }

    [Fact]
    public void EveryCombatEventHasASound()
    {
        foreach (var kind in Enum.GetValues<EventKind>().Where(k => k != EventKind.None))
            Assert.NotNull(Cues.For(new SimEvent { Kind = kind, Move = -1 }));
    }

    [Fact]
    public void PitchVariesSlightlyAndRepeatably()
    {
        var pitches = Enumerable.Range(0, 200).Select(f => SoundDirector.PitchFor(f, Sfx.HitLight, 0, 3)).ToList();
        Assert.All(pitches, p => Assert.InRange(p, 0.95f, 1.05f));
        Assert.True(pitches.Distinct().Count() > 50, "pitch should vary from event to event");
        Assert.Equal(pitches[17], SoundDirector.PitchFor(17, Sfx.HitLight, 0, 3));
    }

    [Fact]
    public void EverySoundIsShortCleanAndTheSameEachTime()
    {
        foreach (var sfx in Enum.GetValues<Sfx>())
        {
            var samples = Synth.Generate(sfx);
            Assert.InRange(samples.Length, Synth.Rate / 20, Synth.Rate); // 50 ms to a second
            Assert.All(samples, s => Assert.InRange(s, -1f, 1f));
            Assert.True(samples.Max(Math.Abs) > 0.5f, $"{sfx} should be loud enough");
            Assert.True(Math.Abs(samples[^1]) < 0.01f, $"{sfx} should end at silence, not click");
            Assert.Equal(samples, Synth.Generate(sfx));
        }
    }
}
