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
// but the match still feels held, not stopped. Presentation only; MusicView plays it.
public sealed class MusicDirector
{
    public const float PausedDuckDb = -10;

    private readonly IMusicSink _sink;
    private float _gainDb = float.NaN;
    private bool _ducked;

    public MusicDirector(IMusicSink sink) => _sink = sink;

    public Track? Playing { get; private set; }

    // When set, a line per change: "display frame 20  music Match", "... music ducked".
    public List<string>? Log { get; set; }

    public static float GainDb(int musicVolume, bool paused) =>
        SoundDirector.VolumeDb(musicVolume) + (paused ? PausedDuckDb : 0);

    public void Update(Track track, int musicVolume, bool paused, int displayFrame)
    {
        var gain = GainDb(musicVolume, paused);
        if (track != Playing)
        {
            Playing = track;
            _gainDb = gain;
            _ducked = paused;
            _sink.Play(track, gain);
            Log?.Add($"display frame {displayFrame,4}  music {track}{(paused ? ", ducked" : "")}");
            return;
        }
        if (gain != _gainDb)
        {
            _gainDb = gain;
            _sink.SetGain(gain);
        }
        if (paused != _ducked)
        {
            _ducked = paused;
            Log?.Add($"display frame {displayFrame,4}  music {(paused ? "ducked" : "back up")}");
        }
    }
}
