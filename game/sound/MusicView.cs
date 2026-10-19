using System.Collections.Generic;
using Godot;

namespace StickFighter.Sound;

// The music through Godot's audio: each loop built once from MusicSynth as looping 16-bit PCM, two
// players so a new track crossfades in over the old. MusicDirector decides what plays.
public partial class MusicView : Node, IMusicSink
{
    private const float CrossfadeSeconds = 0.4f;

    private static Dictionary<Track, AudioStreamWav>? _streams;

    private readonly AudioStreamPlayer[] _players = new AudioStreamPlayer[2];
    private int _current;
    private Tween? _fade;

    public MusicView() => Director = new MusicDirector(this);

    public MusicDirector Director { get; }

    public override void _Ready()
    {
        _streams ??= Build();
        for (var i = 0; i < _players.Length; i++)
        {
            _players[i] = new AudioStreamPlayer();
            AddChild(_players[i]);
        }
    }

    public void Play(Track track, float gainDb)
    {
        var old = _players[_current];
        _current = 1 - _current;
        var next = _players[_current];

        _fade?.Kill();
        _fade = CreateTween().SetParallel();
        if (old.Playing)
        {
            _fade.TweenProperty(old, "volume_db", SoundDirector.SilentDb, CrossfadeSeconds);
            _fade.Chain().TweenCallback(Callable.From(old.Stop));
        }
        next.Stream = _streams![track];
        next.VolumeDb = SoundDirector.SilentDb;
        next.Play();
        _fade.TweenProperty(next, "volume_db", gainDb, CrossfadeSeconds);
    }

    public void SetGain(float gainDb)
    {
        // A change mid-crossfade finishes it at once: the old track stops, the new one takes the level.
        if (_fade?.IsRunning() == true)
        {
            _fade.Kill();
            _players[1 - _current].Stop();
        }
        // A short glide, so ducking under the voice or the pause menu doesn't jump.
        _fade = CreateTween();
        _fade.TweenProperty(_players[_current], "volume_db", gainDb, GlideSeconds);
    }

    private const float GlideSeconds = 0.12f;

    private static Dictionary<Track, AudioStreamWav> Build()
    {
        var streams = new Dictionary<Track, AudioStreamWav>();
        foreach (var track in System.Enum.GetValues<Track>())
        {
            var samples = MusicSynth.Generate(track);
            var data = new byte[samples.Length * 2];
            for (var i = 0; i < samples.Length; i++)
            {
                var s = (short)Mathf.Clamp(Mathf.RoundToInt(samples[i] * short.MaxValue), short.MinValue, short.MaxValue);
                data[2 * i] = (byte)s;
                data[2 * i + 1] = (byte)(s >> 8);
            }
            streams[track] = new AudioStreamWav
            {
                Format = AudioStreamWav.FormatEnum.Format16Bits,
                MixRate = MusicSynth.Rate,
                Stereo = false,
                Data = data,
                LoopMode = AudioStreamWav.LoopModeEnum.Forward,
                LoopBegin = 0,
                LoopEnd = samples.Length,
            };
        }
        return streams;
    }
}
