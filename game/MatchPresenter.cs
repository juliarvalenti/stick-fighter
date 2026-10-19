using Godot;
using StickFighter.Sim;
using StickFighter.UI;

namespace StickFighter;

// The match's HUD, screen-anchored over the stage and filled from the latest state each frame;
// it also keeps the round log for the results screen. Presentation only: it reads the state and
// never writes it.
public partial class MatchPresenter : CanvasLayer
{
    private readonly Hud _hud = new();
    private HealthTrail _trail = new();
    private Announcer _announcer = new();
    private RoundLog _log = new();

    public MatchPresenter()
    {
        Layer = 4;
        Visible = false;
        AddChild(_hud);
    }

    // A new match: no trail, no calls made, no rounds logged. `colors` are the players' color
    // choices (0 own, 1 alternate), so the HUD matches the fighters.
    public void Begin(int[]? colors = null)
    {
        _hud.ColorChoices = colors ?? new[] { 0, 0 };
        _trail = new HealthTrail();
        _announcer = new Announcer();
        _log = new RoundLog();
        Visible = true;
    }

    public void Present(in GameState state)
    {
        if (!Visible)
            return;
        _trail.Record(state);
        MatchHud.Fill(_hud.Model, state, _trail);
        _hud.Refresh();
        _log.Present(state);

        // Each call once, in the log (the announcer's voice keys its own the same way: Sound.VoiceCalls).
        if (_announcer.Present(state) is { } call)
            GD.Print($"announce round {call.Round} {call.Kind} (frame {state.Frame})");
    }

    public MatchResults Results(in GameState state) => _log.Results(state);
}
