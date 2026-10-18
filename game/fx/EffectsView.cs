using Godot;
using StickFighter.Sim;
using StickFighter.Sim.Characters;

namespace StickFighter.FX;

// Plays the sim's events as effects, over the fighters: hit sparks sized by strength, a counter-hit
// spark, the block spark (a guard arc), dust on a knockdown, and the KO flash; and shakes the camera
// for heavy hits, counters, throws and KOs. Presentation only: it reads the state, never writes it.
//
// Rollback-safe through EffectLedger: each effect is keyed by the frame and cause, so it plays once
// however often its frame is simulated, and is cancelled if a rollback takes its cause away. What's
// drawn is a pure function of (effect, frames since it started, where), so a frame always looks the same.
public partial class EffectsView : Node2D
{
    private readonly EffectLedger _ledger = new();
    private StageCamera _camera = null!;
    private GameState _state;

    public void Attach(StageCamera camera) => _camera = camera;

    public override void _Ready() => ZIndex = 5; // over the fighters, under the debug view

    public void Present(in GameState state)
    {
        _state = state;
        _ledger.Update(state);
        foreach (var e in _ledger.Cancelled)
        {
            if (EffectLedger.ShakeFor(e).Frames > 0)
                _camera.CancelShake(e.Frame);
        }
        foreach (var e in _ledger.Started)
        {
            var (amplitude, frames) = EffectLedger.ShakeFor(e);
            if (frames > 0)
                _camera.Shake(amplitude, frames, e.Frame);
        }
        QueueRedraw();
    }

    // A scenario reset or a new match: nothing from before carries over.
    public void Clear()
    {
        _ledger.Clear();
        QueueRedraw();
    }

    public override void _Draw()
    {
        // Fireballs in flight: the loop, in the thrower's color, facing the way it flies. Like
        // everything here, a function of the state (the projectile's frame), so it rolls back too.
        for (var slot = 0; slot < GameState.MaxProjectiles; slot++)
        {
            var projectile = _state.Projectiles[slot];
            if (!projectile.Active)
                continue;
            var facing = projectile.Velocity.X >= Sim.Numerics.Fix.Zero ? 1 : -1;
            var color = projectile.Owner == 0 ? Style.Look.P1 : Style.Look.P2;
            Effects.Draw(this, Effect.Fireball, projectile.Frame, projectile.Position.ToVector2(), facing, color);
        }

        foreach (var e in _ledger.Playing)
        {
            if (EffectOf(e) is not { } effect)
                continue;
            var frame = _ledger.Now - e.Frame;
            var at = e.Point.ToVector2();
            // Which way the attacker faced the contact: the guard arc faces back at it.
            var attackerX = _state.Fighters[e.Attacker].Position.X.ToFloat();
            var facing = at.X >= attackerX ? 1 : -1;
            Effects.Draw(this, effect, frame, at, facing, screen: VisibleRect());
        }
    }

    private static Effect? EffectOf(in SimEvent e)
    {
        var strength = e.Move >= 0 ? Characters.Placeholder.Moves[e.Move].Strength : Strength.Heavy;
        return e.Kind switch
        {
            EventKind.Hit or EventKind.Throw => strength switch
            {
                Strength.Light => Effect.HitLight,
                Strength.Medium => Effect.HitMedium,
                _ => Effect.HitHeavy,
            },
            EventKind.CounterHit => Effect.CounterHit,
            EventKind.Block => Effect.Block,
            EventKind.Knockdown => Effect.Dust,
            EventKind.Ko => Effect.KoFlash,
            EventKind.Clash => Effect.FireballDissipate,
            _ => null,
        };
    }

    // The part of the stage the camera shows, for full-screen effects.
    private Rect2 VisibleRect()
    {
        var size = GetViewportRect().Size / _camera.Zoom;
        return new Rect2(_camera.GetScreenCenterPosition() - size / 2, size);
    }
}
