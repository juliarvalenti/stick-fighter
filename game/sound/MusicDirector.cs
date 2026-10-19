using System.Collections.Generic;

namespace StickFighter.Sound;

// Where music goes: a track starts (crossfading from whatever played), or the level changes.
public interface IMusicSink
{
    void Play(Track track, float gainDb);

    void SetGain(float gainDb);
}

// Which loop plays, and how loud: the menu loop on the title, menus and results, the match loop in
// a match. The level is the Options music volume, ducked while paused so the pause menu is quiet
// but the match still feels held, not stopped, and a little under the announcer so every call is
// heard. Presentation only; MusicView plays it.
public sealed class MusicDirector
{
    public const float PausedDuckDb = -10;
    public const float VoiceDuckDb = -6;

    private enum Duck
    {
        None,
        Voice,
        Paused,
    }

    private readonly IMusicSink _sink;
    private float _gainDb = float.NaN;
    private Duck _duck;

    public MusicDirector(IMusicSink sink) => _sink = sink;

    public Track? Playing { get; private set; }

    // When set, a line per change: "display frame 20  music Match", "... music ducked".
    public List<string>? Log { get; set; }

    public static float GainDb(int musicVolume, bool paused, bool voice = false) =>
        SoundDirector.VolumeDb(musicVolume) + DuckDb(DuckFor(paused, voice));

    public void Update(Track track, int musicVolume, bool paused, int displayFrame, bool voice = false)
    {
        var duck = DuckFor(paused, voice);
        var gain = SoundDirector.VolumeDb(musicVolume) + DuckDb(duck);
        if (track != Playing)
        {
            Playing = track;
            _gainDb = gain;
            _duck = duck;
            _sink.Play(track, gain);
            Log?.Add($"display frame {displayFrame,4}  music {track}{(duck == Duck.Paused ? ", ducked" : duck == Duck.Voice ? ", under the voice" : "")}");
            return;
        }
        if (gain != _gainDb)
        {
            _gainDb = gain;
            _sink.SetGain(gain);
        }
        if (duck != _duck)
        {
            _duck = duck;
            var now = duck switch { Duck.Paused => "ducked", Duck.Voice => "under the voice", _ => "back up" };
            Log?.Add($"display frame {displayFrame,4}  music {now}");
        }
    }

    // Pause wins: under the pause menu the voice doesn't matter.
    private static Duck DuckFor(bool paused, bool voice) => paused ? Duck.Paused : voice ? Duck.Voice : Duck.None;

    private static float DuckDb(Duck duck) => duck switch
    {
        Duck.Paused => PausedDuckDb,
        Duck.Voice => VoiceDuckDb,
        _ => 0,
    };
}
