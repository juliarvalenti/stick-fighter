using System.Collections.Generic;
using Godot;
using StickFighter.Style;

namespace StickFighter.UI;

// Input glyphs in the stick style, drawn in code. One entry point, Glyphs.For("236P"), used by the
// move list, training mode's input display and the tutorial alike.
//
// - Directions are numpad arrows in a ring (5, neutral, is a dot), facing right.
// - Punches are circles and kicks rounded squares, so the two never read alike.
// - Strength is weight: light is an outline, medium is filled, heavy is filled with an outer ring.
//   The letters (LP, MK, ...) are inside too, for anyone who reads rather than scans.
// - "j." is a small up-arc, "+" joins buttons pressed together.
public static partial class Glyphs
{
    public const float DefaultHeight = 40;
    private const float Gap = 4;

    // A control that shows a notation's glyphs, sized to fit. `onPanel` draws light-on-dark.
    public static Control For(string notation, float height = DefaultHeight, bool onPanel = false) =>
        new GlyphStrip(notation, height, onPanel);

    public static float Width(string notation, float height = DefaultHeight)
    {
        var width = 0f;
        foreach (var glyph in Notation.Parse(notation))
            width += WidthOf(glyph, height) + Gap;
        return width - Gap;
    }

    // Draws a notation with its top-left at `at`; returns the width used.
    public static float Draw(CanvasItem item, Vector2 at, string notation, float height, bool onPanel = false) =>
        Draw(item, at, Notation.Parse(notation), height, onPanel);

    // Draws glyphs that aren't a whole move, such as an input-display row that's only a direction.
    public static float Draw(CanvasItem item, Vector2 at, IReadOnlyList<Glyph> glyphs, float height, bool onPanel = false)
    {
        var ink = onPanel ? Look.Text : Look.Ink;
        var paper = onPanel ? Look.Surface : Look.Paper;
        var x = at.X;
        foreach (var glyph in glyphs)
        {
            var w = WidthOf(glyph, height);
            var box = new Rect2(x, at.Y, w, height);
            switch (glyph.Kind)
            {
                case GlyphKind.Direction:
                    DrawDirection(item, box, glyph.Direction, ink);
                    break;
                case GlyphKind.Button:
                    DrawButton(item, box, glyph, ink, paper);
                    break;
                case GlyphKind.Air:
                    DrawAir(item, box, ink);
                    break;
                case GlyphKind.Plus:
                    var c = box.GetCenter();
                    var arm = height * 0.16f;
                    item.Stroke(c - new Vector2(arm, 0), c + new Vector2(arm, 0), ink, Stroke(height));
                    item.Stroke(c - new Vector2(0, arm), c + new Vector2(0, arm), ink, Stroke(height));
                    break;
            }
            x += w + Gap;
        }
        return x - Gap - at.X;
    }

    private static float WidthOf(Glyph glyph, float height) => glyph.Kind switch
    {
        GlyphKind.Plus => height * 0.5f,
        GlyphKind.Air => height * 0.6f,
        _ => height,
    };

    private static float Stroke(float height) => Mathf.Max(2, height * 0.09f);

    // A thin ring with a round-capped arrow inside, pointing the way the stick is held.
    private static void DrawDirection(CanvasItem item, Rect2 box, int direction, Color ink)
    {
        var c = box.GetCenter();
        var r = box.Size.Y / 2 - 1;
        var w = Stroke(box.Size.Y);
        item.DrawArc(c, r - w / 2, 0, Mathf.Tau, 48, ink, w * 0.6f, true);

        var (vx, vy) = Notation.Vector(direction);
        if (vx == 0 && vy == 0)
        {
            item.DrawCircle(c, r * 0.22f, ink);
            return;
        }
        var v = new Vector2(vx, vy).Normalized();
        var tail = c - v * r * 0.45f;
        var tip = c + v * r * 0.5f;
        item.Stroke(tail, tip, ink, w);
        var side = v.Orthogonal() * r * 0.32f;
        var back = tip - v * r * 0.32f;
        item.Stroke(tip, back + side, ink, w);
        item.Stroke(tip, back - side, ink, w);
    }

    // Circle for a punch, rounded square for a kick; outline, filled or ringed by strength.
    private static void DrawButton(CanvasItem item, Rect2 box, Glyph glyph, Color ink, Color paper)
    {
        var w = Stroke(box.Size.Y);
        var heavy = glyph.Strength == Strength.Heavy;
        var shape = heavy ? box.Grow(-w * 1.6f) : box.Grow(-1);
        var punch = glyph.Attack == Attack.Punch;
        var filled = glyph.Strength is Strength.Medium or Strength.Heavy or Strength.Any;

        if (heavy)
            Outline(item, box.Grow(-1), punch, ink, w * 0.7f);
        if (filled)
            Fill(item, shape, punch, ink);
        else
            Outline(item, shape, punch, ink, w);

        var letter = (glyph.Strength switch
        {
            Strength.Light => "L",
            Strength.Medium => "M",
            Strength.Heavy => "H",
            _ => "",
        }) + (punch ? "P" : "K");
        var size = (int)(box.Size.Y * (letter.Length > 1 ? 0.36f : 0.5f));
        item.Text(shape, letter, Look.UiBold, size, filled ? paper : ink);
    }

    private static void Fill(CanvasItem item, Rect2 shape, bool round, Color color)
    {
        if (round)
            item.DrawCircle(shape.GetCenter(), shape.Size.Y / 2, color);
        else
            item.Card(shape, color, shape.Size.Y * 0.28f);
    }

    private static void Outline(CanvasItem item, Rect2 shape, bool round, Color color, float width)
    {
        if (round)
            item.DrawArc(shape.GetCenter(), shape.Size.Y / 2 - width / 2, 0, Mathf.Tau, 48, color, width, true);
        else
            item.CardOutline(shape, color, width, shape.Size.Y * 0.28f);
    }

    // A small arc rising to the right with a dot at its peak: off the ground.
    private static void DrawAir(CanvasItem item, Rect2 box, Color ink)
    {
        var w = Stroke(box.Size.Y);
        var points = new List<Vector2>();
        for (var i = 0; i <= 12; i++)
        {
            var t = i / 12f;
            points.Add(new Vector2(box.Position.X + w + t * (box.Size.X - 2 * w),
                box.End.Y - w - Mathf.Sin(t * Mathf.Pi) * box.Size.Y * 0.55f));
        }
        item.DrawPolyline(points.ToArray(), ink, w * 0.8f, true);
        item.DrawCircle(new Vector2(box.GetCenter().X, box.End.Y - w - box.Size.Y * 0.55f - w * 1.4f), w * 1.1f, ink);
    }

    private sealed partial class GlyphStrip : Control
    {
        private readonly string _notation;
        private readonly float _height;
        private readonly bool _onPanel;

        public GlyphStrip(string notation, float height, bool onPanel)
        {
            _notation = notation;
            _height = height;
            _onPanel = onPanel;
            CustomMinimumSize = new Vector2(Width(notation, height), height);
            MouseFilter = MouseFilterEnum.Ignore;
        }

        public override void _Draw() => Glyphs.Draw(this, Vector2.Zero, _notation, _height, _onPanel);
    }
}
