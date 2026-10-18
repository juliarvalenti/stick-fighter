using Godot;
using StickFighter.Style;

namespace StickFighter.FX;

public enum Effect
{
    HitLight,
    HitMedium,
    HitHeavy,
    CounterHit,
    Block,
    Fireball, // loops for as long as the projectile lives
    FireballDissipate,
    Dust,
    KoFlash, // full screen
    SuperFlash, // full screen
}

// The game's effects, drawn in code in the stick style: flat fills and round-capped strokes, each
// a short animation indexed by frame. The presentation plays one from a contact point by asking for
// frame N of it, so a frame's picture is a pure function of (effect, frame, where): repeatable in
// screenshots and safe under rollback (replaying a frame redraws the same thing).
//
// Sparks are bursts of rays with an ink edge under the color, so they read on paper and on a
// fighter alike. Block is a different shape (a guard arc facing the attacker), not a recolor, so it
// reads as "blocked" even at a glance.
public static class Effects
{
    public static int Length(Effect effect) => effect switch
    {
        Effect.HitLight => 8,
        Effect.HitMedium => 10,
        Effect.HitHeavy => 12,
        Effect.CounterHit => 14,
        Effect.Block => 10,
        Effect.Fireball => 12,
        Effect.FireballDissipate => 10,
        Effect.Dust => 14,
        Effect.KoFlash => 18,
        _ => 24,
    };

    // Draws frame `frame` of `effect` at `at`. `facing` is the attacker's (+1 facing right);
    // `color` is the owner's, for projectiles; `screen` is the area full-screen effects cover.
    public static void Draw(CanvasItem item, Effect effect, int frame, Vector2 at, int facing = 1, Color? color = null,
        Rect2? screen = null)
    {
        var n = Length(effect);
        if (effect != Effect.Fireball && (frame < 0 || frame >= n))
            return;
        var t = n > 1 ? frame / (float)(n - 1) : 1;
        switch (effect)
        {
            case Effect.HitLight:
                Burst(item, at, t, frame, radius: 34, rays: 6, width: 7, Look.Accent, rings: 0);
                break;
            case Effect.HitMedium:
                Burst(item, at, t, frame, radius: 48, rays: 8, width: 9, Look.Accent, rings: 0);
                break;
            case Effect.HitHeavy:
                Burst(item, at, t, frame, radius: 66, rays: 10, width: 11, Look.Accent, rings: 1);
                break;
            case Effect.CounterHit:
                Burst(item, at, t, frame, radius: 74, rays: 12, width: 11, Look.Danger, rings: 2);
                break;
            case Effect.Block:
                Guard(item, at, t, facing);
                break;
            case Effect.Fireball:
                Fireball(item, at, frame % n, facing, color ?? Look.P1);
                break;
            case Effect.FireballDissipate:
                Dissipate(item, at, t, color ?? Look.P1);
                break;
            case Effect.Dust:
                Dust(item, at, t, facing);
                break;
            case Effect.KoFlash:
                KoFlash(item, at, frame, t, screen ?? new Rect2(0, 0, 1280, 720));
                break;
            default:
                SuperFlash(item, at, frame, screen ?? new Rect2(0, 0, 1280, 720));
                break;
        }
    }

    // Fixed per-ray jitter, so a burst isn't a perfect star but is the same every time.
    private static readonly float[] Jitter = { 0.0f, 0.31f, -0.22f, 0.12f, -0.35f, 0.27f, -0.08f, 0.19f, -0.28f, 0.05f, 0.33f, -0.15f };
    private static readonly float[] Reach = { 1.0f, 0.72f, 0.9f, 0.62f, 0.96f, 0.7f, 0.85f, 0.66f, 1.0f, 0.75f, 0.88f, 0.6f };

    // A hit: a white core that flashes and shrinks, rays that shoot out and thin, and for heavier
    // hits rings that expand.
    private static void Burst(CanvasItem item, Vector2 at, float t, int frame, float radius, int rays, float width, Color color, int rings)
    {
        var outT = EaseOut(Mathf.Min(1, t * 1.8f));
        var w = width * (1 - 0.75f * t);
        for (var i = 0; i < rays; i++)
        {
            var angle = (i + Jitter[i % Jitter.Length]) * Mathf.Tau / rays;
            var dir = Vector2.FromAngle(angle);
            var length = radius * Reach[i % Reach.Length];
            var inner = length * (0.2f + 0.8f * EaseOut(t));
            var outer = length * (0.45f + 0.55f * outT);
            if (outer - inner < 1)
                continue;
            item.Stroke(at + dir * inner, at + dir * outer, Look.Ink, w + 4);
            item.Stroke(at + dir * inner, at + dir * outer, color, w);
        }

        for (var r = 0; r < rings; r++)
        {
            var k = Mathf.Clamp(t * 1.4f - r * 0.2f, 0, 1);
            if (k <= 0 || k >= 1)
                continue;
            item.DrawArc(at, radius * (0.5f + 0.7f * EaseOut(k)), 0, Mathf.Tau, 48, color, width * (1 - k) + 1, true);
        }

        if (frame <= 2)
        {
            var core = radius * 0.42f * (1 - frame / 3f);
            item.DrawCircle(at, core + 3, Look.Ink);
            item.DrawCircle(at, core, Look.Text);
        }
    }

    // A block: three guard arcs facing the attacker that push back and thin, with short chips
    // flying off behind.
    private static void Guard(CanvasItem item, Vector2 at, float t, int facing)
    {
        var toward = facing > 0 ? Mathf.Pi : 0; // the arcs face back toward the attacker
        var spread = 1.1f;
        for (var i = 0; i < 3; i++)
        {
            var k = Mathf.Clamp(t * 1.3f - i * 0.12f, 0, 1);
            if (k >= 1)
                continue;
            var r = 18 + i * 12 + 22 * EaseOut(k);
            var w = 7 * (1 - k) + 1;
            item.DrawArc(at, r, toward - spread, toward + spread, 24, Look.Ink, w + 4, true);
            item.DrawArc(at, r, toward - spread, toward + spread, 24, Look.Text, w, true);
        }
        for (var i = 0; i < 4; i++)
        {
            var angle = toward + Mathf.Pi + (i - 1.5f) * 0.45f;
            var dir = Vector2.FromAngle(angle);
            var inner = 10 + 40 * EaseOut(t);
            var outer = inner + 14 * (1 - t);
            if (outer - inner < 1)
                continue;
            item.Stroke(at + dir * inner, at + dir * outer, Look.Ink, 5);
            item.Stroke(at + dir * inner, at + dir * outer, Look.Text, 2.5f);
        }
    }

    private static readonly float[] Flicker = { 0, 2, 3, 1, -1, 2, 4, 1, 0, 3, 1, -2 };

    // A fireball: a ball in the owner's color with a pale core, and three round-capped streaks
    // trailing behind that flicker in length.
    private static void Fireball(CanvasItem item, Vector2 at, int frame, int facing, Color color)
    {
        var back = new Vector2(-facing, 0);
        for (var i = 0; i < 3; i++)
        {
            var y = (i - 1) * 12f;
            var length = 34 + Flicker[(frame + i * 4) % Flicker.Length] * 4 - i % 2 * 10;
            var from = at + new Vector2(0, y) + back * 10;
            item.Stroke(from, from + back * length, Look.Ink, 12);
            item.Stroke(from, from + back * length, color, 8);
        }
        var r = 22 + Flicker[frame % Flicker.Length] * 0.5f;
        item.DrawCircle(at, r + 3, Look.Ink);
        item.DrawCircle(at, r, color);
        item.DrawCircle(at + new Vector2(facing * 4, -3), r * 0.42f, Look.Text);
    }

    // A fireball breaking up: a ring that grows and thins, and pieces flying apart.
    private static void Dissipate(CanvasItem item, Vector2 at, float t, Color color)
    {
        var ring = 22 + 30 * EaseOut(t);
        var w = 8 * (1 - t) + 1;
        item.DrawArc(at, ring, 0, Mathf.Tau, 48, Look.Ink, w + 4, true);
        item.DrawArc(at, ring, 0, Mathf.Tau, 48, color, w, true);
        for (var i = 0; i < 6; i++)
        {
            var dir = Vector2.FromAngle((i + Jitter[i]) * Mathf.Tau / 6);
            var p = at + dir * (10 + 40 * EaseOut(t));
            var r = 7 * (1 - t);
            if (r < 0.5f)
                continue;
            item.DrawCircle(p, r + 2, Look.Ink);
            item.DrawCircle(p, r, color);
        }
    }

    private static readonly Vector2[] Puffs = { new(-26, -4), new(-10, -10), new(8, -6), new(24, -2) };

    // Dust at the feet: four puffs that swell, drift out and up, then shrink away.
    private static void Dust(CanvasItem item, Vector2 at, float t, int facing)
    {
        for (var i = 0; i < Puffs.Length; i++)
        {
            var p = Puffs[i];
            var drift = new Vector2(p.X * 0.6f * t, -10 * t);
            var r = 18 * Mathf.Sin(Mathf.Pi * Mathf.Clamp(t * 1.1f + i * 0.04f, 0, 1)) * (0.7f + 0.1f * i);
            if (r < 0.5f)
                continue;
            item.DrawCircle(at + new Vector2(p.X * facing, p.Y) + drift, r, Look.Dust);
        }
    }

    // K.O.: a white frame, an ink frame, then the ink falling away while red rays shoot from the
    // finishing blow across the screen.
    private static void KoFlash(CanvasItem item, Vector2 at, int frame, float t, Rect2 screen)
    {
        if (frame == 0)
        {
            item.DrawRect(screen, Look.Text);
            return;
        }
        var ink = frame == 1 ? 0.85f : Mathf.Max(0, 0.6f * (1 - t * 1.4f));
        if (ink > 0)
            item.DrawRect(screen, new Color(Look.Ink, ink));
        var reach = screen.Size.Length();
        var w = Mathf.Max(1, screen.Size.Y * 0.02f * (1 - t));
        for (var i = 0; i < 12; i++)
        {
            var dir = Vector2.FromAngle((i + Jitter[i]) * Mathf.Tau / 12);
            var inner = reach * (0.05f + 0.6f * EaseOut(t));
            var outer = reach * (0.15f + 0.85f * EaseOut(Mathf.Min(1, t * 2)));
            if (outer - inner < 1)
                continue;
            if (Clip(at + dir * inner, at + dir * outer, screen) is { } ray)
                item.Stroke(ray.A, ray.B, Look.Danger, w);
        }
    }

    // A super starting: the screen dims for the freeze, an accent frame snaps in round the screen,
    // and speed lines turn slowly round the fighter.
    private static void SuperFlash(CanvasItem item, Vector2 at, int frame, Rect2 screen)
    {
        item.DrawRect(screen, new Color(Look.Ink, 0.75f));
        var snap = Mathf.Clamp(frame / 4f, 0, 1);
        var inset = screen.Size.Y * 0.022f;
        var frameRect = screen.Grow(-inset + (1 - EaseOut(snap)) * inset * 2);
        var box = new StyleBoxFlat { DrawCenter = false, BorderColor = Look.Accent, AntiAliasing = true };
        box.SetBorderWidthAll(Mathf.Max(2, (int)(screen.Size.Y * 0.014f)));
        box.SetCornerRadiusAll((int)(screen.Size.Y * 0.03f));
        item.DrawStyleBox(box, frameRect);

        var reach = screen.Size.Length() * 0.6f;
        var w = Mathf.Max(1, screen.Size.Y * 0.004f);
        for (var i = 0; i < 16; i++)
        {
            var angle = (i + Jitter[i % Jitter.Length]) * Mathf.Tau / 16 + frame * 0.02f;
            var dir = Vector2.FromAngle(angle);
            var inner = reach * (0.18f + 0.1f * Reach[i % Reach.Length]);
            if (Clip(at + dir * inner, at + dir * reach, screen) is { } line)
                item.Stroke(line.A, line.B, Look.Accent, w);
        }
    }

    private static float EaseOut(float t) => 1 - (1 - t) * (1 - t);

    // The part of segment a-b inside `rect` (Liang-Barsky), or null if none: full-screen effects
    // stay on the screen they're given.
    private static (Vector2 A, Vector2 B)? Clip(Vector2 a, Vector2 b, Rect2 rect)
    {
        var d = b - a;
        float t0 = 0, t1 = 1;
        foreach (var (p, q) in new[]
        {
            (-d.X, a.X - rect.Position.X), (d.X, rect.End.X - a.X),
            (-d.Y, a.Y - rect.Position.Y), (d.Y, rect.End.Y - a.Y),
        })
        {
            if (p == 0)
            {
                if (q < 0)
                    return null;
                continue;
            }
            var r = q / p;
            if (p < 0)
                t0 = Mathf.Max(t0, r);
            else
                t1 = Mathf.Min(t1, r);
            if (t0 > t1)
                return null;
        }
        return (a + d * t0, a + d * t1);
    }
}
