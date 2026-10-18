using Godot;

namespace StickFighter.Style;

// The shapes the style is made of, drawn the same way everywhere: round-capped strokes (a limb),
// pills (a bar or a tag), cards, and text placed by its box.
public static class Paint
{
    // A line with round caps, like a limb.
    public static void Stroke(this CanvasItem item, Vector2 from, Vector2 to, Color color, float width)
    {
        item.DrawLine(from, to, color, width);
        item.DrawCircle(from, width / 2, color);
        item.DrawCircle(to, width / 2, color);
    }

    // A filled rectangle with fully round ends. Zero-width pills draw nothing.
    public static void Pill(this CanvasItem item, Rect2 rect, Color color)
    {
        if (rect.Size.X <= 0 || rect.Size.Y <= 0)
            return;
        var r = Mathf.Min(rect.Size.X, rect.Size.Y) / 2;
        Card(item, rect, color, r);
    }

    // A filled rectangle with round corners.
    public static void Card(this CanvasItem item, Rect2 rect, Color color, float radius = Look.CardRadius)
    {
        var box = new StyleBoxFlat { BgColor = color, AntiAliasing = true };
        box.SetCornerRadiusAll((int)radius);
        item.DrawStyleBox(box, rect);
    }

    // The outline of a card, for focus rings and empty slots.
    public static void CardOutline(this CanvasItem item, Rect2 rect, Color color, float width = Look.BoldStroke,
        float radius = Look.CardRadius)
    {
        var box = new StyleBoxFlat { DrawCenter = false, BorderColor = color, AntiAliasing = true };
        box.SetBorderWidthAll((int)width);
        box.SetCornerRadiusAll((int)radius);
        item.DrawStyleBox(box, rect);
    }

    // Text centered vertically in a box, aligned horizontally within it.
    public static void Text(this CanvasItem item, Rect2 box, string text, Font font, int size, Color color,
        HorizontalAlignment align = HorizontalAlignment.Center)
    {
        var ascent = font.GetAscent(size);
        var height = font.GetHeight(size);
        var baseline = box.Position.Y + (box.Size.Y - height) / 2 + ascent;
        item.DrawString(font, new Vector2(box.Position.X, baseline), text, align, box.Size.X, size, color);
    }
}
