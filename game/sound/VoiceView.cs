using Godot;
using StickFighter.Sim;

namespace StickFighter.Sound;

// The announcer through Godot's audio: one player, the committed lines (assets/audio/announcer/).
// VoiceCalls decides what's said, VoiceDirector when; this plays it. Presentation only.
public partial class VoiceView : Node, IVoiceSink
{
    private const float StopSeconds = 0.04f;

    private readonly AudioStreamPlayer _player = new();
    private VoiceCalls _calls = new();
    private Tween? _stop;

    public VoiceView() => Director = new VoiceDirector(this);

    public VoiceDirector Director { get; }

    public override void _Ready() => AddChild(_player);

    // A new match: no calls made yet, and nothing still speaking.
    public void Begin()
    {
        _calls = new VoiceCalls();
        Director.Clear();
    }

    public void Present(in GameState state, int volume, int displayFrame)
    {
        Director.Volume = volume;
        if (_calls.Present(state) is { } line)
            Director.Call(line, displayFrame);
        Director.Update(displayFrame);
    }

    public float Play(VoiceLine line, float gainDb)
    {
        _stop?.Kill();
        var stream = GD.Load<AudioStream>($"res://assets/audio/announcer/{VoiceLines.File(line)}.mp3");
        _player.Stream = stream;
        _player.VolumeDb = gainDb;
        _player.Play();
        return (float)stream.GetLength();
    }

    public void Stop()
    {
        if (!_player.Playing)
            return;
        _stop?.Kill();
        _stop = CreateTween();
        _stop.TweenProperty(_player, "volume_db", SoundDirector.SilentDb, StopSeconds);
        _stop.TweenCallback(Callable.From(_player.Stop));
    }
}
