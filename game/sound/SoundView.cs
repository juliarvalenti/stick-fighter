using System.Collections.Generic;
using Godot;
using StickFighter.Sim;

namespace StickFighter.Sound;

// The combat sounds through Godot's audio: a player per voice, each sound built once from Synth as
// 16-bit PCM. SoundDirector decides what plays; this only plays it. Presentation only.
public partial class SoundView : Node, IAudioSink
{
    // How long a cancelled sound takes to fade (long enough not to click, short enough to be gone).
    private const float FadeSeconds = 0.04f;

    private static Dictionary<Sfx, AudioStreamWav>? _streams;

    private readonly AudioStreamPlayer[] _players = new AudioStreamPlayer[SoundDirector.Voices];
    private readonly Tween?[] _fades = new Tween?[SoundDirector.Voices];

    public SoundView() => Director = new SoundDirector(this);

    public SoundDirector Director { get; }

    public override void _Ready()
    {
        _streams ??= Build();
        for (var v = 0; v < _players.Length; v++)
        {
            _players[v] = new AudioStreamPlayer();
            AddChild(_players[v]);
        }
    }

    public void Present(in GameState state, int effectsVolume)
    {
        Director.EffectsVolume = effectsVolume;
        Director.Update(state);
    }

    public void Play(int voice, Sfx sfx, float pitch, float gainDb)
    {
        _fades[voice]?.Kill();
        var player = _players[voice];
        player.Stream = _streams![sfx];
        player.PitchScale = pitch;
        player.VolumeDb = gainDb;
        player.Play();
    }

    public void Fade(int voice)
    {
        var player = _players[voice];
        if (!player.Playing)
            return;
        _fades[voice]?.Kill();
        var fade = CreateTween();
        fade.TweenProperty(player, "volume_db", SoundDirector.SilentDb, FadeSeconds);
        fade.TweenCallback(Callable.From(player.Stop));
        _fades[voice] = fade;
    }

    private static Dictionary<Sfx, AudioStreamWav> Build()
    {
        var streams = new Dictionary<Sfx, AudioStreamWav>();
        foreach (var sfx in System.Enum.GetValues<Sfx>())
        {
            var samples = Synth.Generate(sfx);
            var data = new byte[samples.Length * 2];
            for (var i = 0; i < samples.Length; i++)
            {
                var s = (short)Mathf.Clamp(Mathf.RoundToInt(samples[i] * short.MaxValue), short.MinValue, short.MaxValue);
                data[2 * i] = (byte)s;
                data[2 * i + 1] = (byte)(s >> 8);
            }
            streams[sfx] = new AudioStreamWav
            {
                Format = AudioStreamWav.FormatEnum.Format16Bits,
                MixRate = Synth.Rate,
                Stereo = false,
                Data = data,
            };
        }
        return streams;
    }
}
