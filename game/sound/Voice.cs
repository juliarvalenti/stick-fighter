using System.Collections.Generic;
using StickFighter.Sim;
using StickFighter.UI;

namespace StickFighter.Sound;

// The announcer's lines: assets/audio/announcer/<File>.mp3 (see SOURCE.md there).
public enum VoiceLine
{
    Round1,
    Round2,
    Round3,
    FinalRound,
    Fight,
    KO,
    Time,
    Perfect,
    DoubleKO,
    Draw,
    P1Wins,
    P2Wins,
}

public static class VoiceLines
{
    public static string File(VoiceLine line) => line switch
    {
        VoiceLine.Round1 => "round-1",
        VoiceLine.Round2 => "round-2",
        VoiceLine.Round3 => "round-3",
        VoiceLine.FinalRound => "final-round",
        VoiceLine.Fight => "fight",
        VoiceLine.KO => "ko",
        VoiceLine.Time => "time",
        VoiceLine.Perfect => "perfect",
        VoiceLine.DoubleKO => "double-ko",
        VoiceLine.Draw => "draw",
        VoiceLine.P1Wins => "p1-wins",
        _ => "p2-wins",
    };

    // The line for a HUD call. The round call is "Final round" for the round that settles a tie at
    // match point (the sim's FinalRound), otherwise its number.
    public static VoiceLine For(Announcement kind, int round, bool finalRound) => kind switch
    {
        Announcement.Round when finalRound || round > 3 => VoiceLine.FinalRound,
        Announcement.Round => round switch { 1 => VoiceLine.Round1, 2 => VoiceLine.Round2, _ => VoiceLine.Round3 },
        Announcement.Fight => VoiceLine.Fight,
        Announcement.KO => VoiceLine.KO,
        Announcement.Time => VoiceLine.Time,
        Announcement.Perfect => VoiceLine.Perfect,
        _ => VoiceLine.DoubleKO,
    };

    // The match's result: the winner, or a draw (a tied final round).
    public static VoiceLine? ForResult(MatchWinner winner) => winner switch
    {
        MatchWinner.P1 => VoiceLine.P1Wins,
        MatchWinner.P2 => VoiceLine.P2Wins,
        MatchWinner.Draw => VoiceLine.Draw,
        _ => null,
    };
}

// What the announcer says, once each: the HUD's calls (Announcer, already keyed by round and kind,
// so rollback never repeats one) and the result when the match ends. One per match; a new match
// starts a new one.
public sealed class VoiceCalls
{
    private readonly Announcer _announcer = new();
    private bool _result;

    public VoiceLine? Present(in GameState state)
    {
        if (_announcer.Present(state) is { } call)
            return VoiceLines.For(call.Kind, call.Round, state.Match.FinalRound);
        if (_result || state.Match is not { Mode: MatchMode.Versus, Phase: MatchPhase.MatchEnd })
            return null;
        _result = true;
        return VoiceLines.ForResult(state.Match.Winner);
    }
}

// Where the voice goes: a line plays (its length in seconds comes back), or what's playing stops.
public interface IVoiceSink
{
    float Play(VoiceLine line, float gainDb);

    void Stop();
}

// Plays the announcer one line at a time, on the Options announcer volume. A call that comes while
// a line is still going waits for it, but no more than half a second, so "Final round" (longer than
// the round call it voices) finishes before "Fight!", and nothing falls far behind the screen.
// Timed in display frames.
public sealed class VoiceDirector
{
    public const int MaxWaitFrames = 30;

    private readonly IVoiceSink _sink;
    private int _until = int.MinValue;
    private (VoiceLine Line, int Frame)? _waiting;

    public VoiceDirector(IVoiceSink sink) => _sink = sink;

    public int Volume { get; set; } = 80;

    // When set, a line per line spoken: "display frame 12  voice round-1 (0.72 s)".
    public List<string>? Log { get; set; }

    // A line is playing (or waiting): the music ducks under it.
    public bool Speaking(int frame) => frame < _until || _waiting is not null;

    public void Call(VoiceLine line, int frame)
    {
        if (frame < _until)
            _waiting = (line, frame);
        else
            Start(line, frame);
    }

    // Each display frame: a waiting line starts once the one before finishes, or has waited enough.
    public void Update(int frame)
    {
        if (_waiting is not { } w || frame < _until && frame - w.Frame < MaxWaitFrames)
            return;
        _waiting = null;
        if (frame < _until)
            _sink.Stop();
        Start(w.Line, frame);
    }

    // A new match, or back to the menus: nothing carries over.
    public void Clear()
    {
        if (_until != int.MinValue)
            _sink.Stop();
        _until = int.MinValue;
        _waiting = null;
    }

    private void Start(VoiceLine line, int frame)
    {
        var seconds = _sink.Play(line, SoundDirector.VolumeDb(Volume));
        _until = frame + (int)System.MathF.Ceiling(seconds * 60);
        Log?.Add($"display frame {frame,4}  voice {VoiceLines.File(line)} ({seconds:0.00} s)");
    }
}
