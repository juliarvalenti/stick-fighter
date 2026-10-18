using Godot;
using StickFighter.Style;

namespace StickFighter.UI;

// The in-match HUD, drawn from a HudModel and nothing else (presentation only).
//
// Layout at 1280x720: the bars, timer, names and round pips along the top; super meters in the
// bottom corners, under the floor line; the combo counter and callouts at the attacker's edge; and
// announcements centered above head height. All of it stays clear of the fighters' heads at normal
// framing, and stays readable over any part of the stage: bars and tags sit on dark tracks and
// pills, and big text carries an outline.
public partial class Hud : Control
{
    private const float Margin = 48;
    private const float BarTop = 28;
    private const float BarHeight = 32;
    private const float BarInset = 4;
    private const float StunHeight = 10;
    private const float TimerWidth = 112;
    private const float LowHealthShare = 0.25f;

    public HudModel Model { get; set; } = new();

    // Each player's color choice (0 their own, 1 their alternate), so everything player-colored here
    // matches the fighter on screen: name tags, meters, the combo count.
    public int[] ColorChoices { get; set; } = { 0, 0 };

    private Color PlayerColor(int index) => Look.Player(index, ColorChoices[index]);

    public Hud()
    {
        Size = new Vector2(1280, 720);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public void Refresh() => QueueRedraw();

    public override void _Draw()
    {
        var barWidth = (Size.X - TimerWidth) / 2 - Margin - 16;
        for (var p = 0; p < 2; p++)
        {
            var player = Model.Players[p];
            var mirrored = p == 1;
            var x = mirrored ? Size.X - Margin - barWidth : Margin;
            DrawHealth(new Rect2(x, BarTop, barWidth, BarHeight), player, mirrored);
            DrawStun(new Rect2(x, BarTop + BarHeight + 6, barWidth, StunHeight), player, mirrored);
            DrawNameplate(p, player, new Vector2(mirrored ? Size.X - Margin : Margin, BarTop + BarHeight + StunHeight + 16), mirrored);
            DrawMeter(p, player, new Vector2(mirrored ? Size.X - Margin : Margin, Size.Y - 64), mirrored);
            if (player.Callout is { } callout)
                DrawCallout(callout, mirrored);
        }

        DrawTimer();
        if (Model.Combo is { } combo)
            DrawCombo(combo);
        if (Model.Announcement is { } announcement)
            DrawAnnouncement(announcement);
    }

    // A dark pill track; the health left anchored at the outer end; the damage just taken
    // trailing in paper until it drains. Low health turns the fill to the LowHealth token.
    private void DrawHealth(Rect2 bar, HudPlayer player, bool mirrored)
    {
        this.Pill(bar, Look.Surface);
        var inner = bar.Grow(-BarInset);
        var health = Share(player.Health, player.MaxHealth);
        var recent = Mathf.Max(health, Share(player.RecentHealth, player.MaxHealth));
        this.Pill(Span(inner, recent, mirrored), Look.Text);
        this.Pill(Span(inner, health, mirrored), health <= LowHealthShare ? Look.LowHealth : Look.Accent);
    }

    // A thin bar that fills from the outer end as stun builds; full stun is dizzy.
    private void DrawStun(Rect2 bar, HudPlayer player, bool mirrored)
    {
        this.Pill(bar, Look.Surface);
        var stun = Share(player.Stun, player.MaxStun);
        this.Pill(Span(bar.Grow(-2), stun, mirrored), Look.Text);
    }

    private void DrawTimer()
    {
        var box = new Rect2((Size.X - TimerWidth) / 2, BarTop - 12, TimerWidth, 88);
        this.Card(box, Look.Surface);
        var text = Model.Timer is { } seconds ? Mathf.Clamp(seconds, 0, 99).ToString() : "--";
        this.Text(box, text, Look.Display, Look.DisplayMedium, Look.Accent);
    }

    // The player's name tag in their color, then round-win pips toward the middle.
    private void DrawNameplate(int index, HudPlayer player, Vector2 anchor, bool mirrored)
    {
        var size = new Vector2(168, 32);
        var tag = new Rect2(mirrored ? anchor.X - size.X : anchor.X, anchor.Y, size.X, size.Y);
        this.Pill(tag, PlayerColor(index));
        this.Text(tag, $"P{index + 1}  {player.Name}", Look.UiBold, Look.UiMedium, Look.Text);

        const float pip = 10;
        for (var i = 0; i < Model.RoundsToWin; i++)
        {
            var step = 16 + pip + i * (pip * 2 + 10);
            var center = new Vector2(mirrored ? tag.Position.X - step : tag.End.X + step, tag.GetCenter().Y);
            DrawCircle(center, pip + 2, Look.Paper); // a paper backing keeps pips readable over the stage
            if (i < player.RoundWins)
                DrawCircle(center, pip, Look.Ink);
            else
                DrawArc(center, pip - 2, 0, Mathf.Tau, 32, Look.Ink, Look.LineStroke, true);
        }
    }

    // The super meter: stocks so far as a number, and a track divided into stocks that fills in the
    // player's color. With no meter yet, the slot is an empty track.
    private void DrawMeter(int index, HudPlayer player, Vector2 anchor, bool mirrored)
    {
        const float width = 260;
        var track = new Rect2(mirrored ? anchor.X - 48 - width : anchor.X + 48, anchor.Y, width, 20);
        this.Pill(track, Look.Surface);
        var stocks = Mathf.Max(1, player.MeterStocks);
        for (var i = 1; i < stocks; i++)
        {
            var x = track.Position.X + track.Size.X * i / stocks;
            DrawLine(new Vector2(x, track.Position.Y + 4), new Vector2(x, track.End.Y - 4), Look.SurfaceRaised, 2);
        }

        var levelBox = new Rect2(mirrored ? anchor.X - 40 : anchor.X, anchor.Y - 10, 40, 40);
        if (player.Meter is not { } meter)
        {
            this.Text(levelBox, "-", Look.Display, Look.UiLarge, Look.InkMuted);
            return;
        }
        this.Pill(Span(track.Grow(-4), (float)(meter / stocks), mirrored), PlayerColor(index));
        this.Text(levelBox, ((int)meter).ToString(), Look.Display, Look.UiLarge, Look.Ink);
    }

    // The combo counter at the attacker's edge: hits big in their color, damage under it.
    private void DrawCombo(HudCombo combo)
    {
        var mirrored = combo.Player == 1;
        var align = mirrored ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        var x = mirrored ? Size.X - Margin - 240 : Margin;
        var color = PlayerColor(combo.Player);
        Outlined(new Rect2(x, 150, 240, 96), combo.Hits.ToString(), Look.Display, Look.DisplayLarge, color, Look.Paper, align);
        Outlined(new Rect2(x, 238, 240, 30), "HITS", Look.UiBold, Look.UiMedium, Look.Ink, Look.Paper, align);
        Outlined(new Rect2(x, 266, 240, 26), $"{combo.Damage} DAMAGE", Look.UiBold, Look.UiSmall, Look.InkMuted, Look.Paper, align);
    }

    // "COUNTER", "REVERSAL": an accent pill under the combo counter on that player's side.
    private void DrawCallout(string callout, bool mirrored)
    {
        var size = new Vector2(Look.UiBold.GetStringSize(callout, HorizontalAlignment.Left, -1, Look.UiMedium).X + 40, 36);
        var pill = new Rect2(mirrored ? Size.X - Margin - size.X : Margin, 300, size.X, size.Y);
        this.Pill(pill, Look.Accent);
        this.Text(pill, callout, Look.UiBold, Look.UiMedium, Look.Ink);
    }

    // An announcement pops in with the style guide's pop motion (scale overshoot), holds, then
    // leaves with the exit motion (fade and a slight grow). Timed from its age, so a frame's
    // picture is repeatable.
    private void DrawAnnouncement(HudAnnouncement a)
    {
        var (text, fill, outline, size) = a.Kind switch
        {
            Announcement.Round => ($"ROUND {a.Round}", Look.Ink, Look.Paper, Look.DisplayLarge),
            Announcement.Fight => ("FIGHT!", Look.Accent, Look.Ink, Look.DisplayHero),
            Announcement.KO => ("K.O.", Look.Danger, Look.Ink, Look.DisplayHero),
            Announcement.Time => ("TIME", Look.Ink, Look.Paper, Look.DisplayHero),
            Announcement.Perfect => ("PERFECT", Look.Accent, Look.Ink, Look.DisplayLarge),
            _ => ("DOUBLE K.O.", Look.Danger, Look.Ink, Look.DisplayLarge),
        };

        var (scale, alpha) = AnnouncementMotion(a.Age / 60.0, a.Life / 60.0);
        if (alpha <= 0)
            return;
        var center = new Vector2(Size.X / 2, 232);
        DrawSetTransform(center, 0, new Vector2(scale, scale));
        var box = new Rect2(-Size.X / 2, -size * 0.7f, Size.X, size * 1.4f);
        Outlined(box, text, Look.Display, size, new Color(fill, alpha), new Color(outline, alpha), HorizontalAlignment.Center, 16);
        DrawSetTransform(Vector2.Zero);
    }

    // Scale and opacity of an announcement `t` seconds into a `life`-second showing.
    public static (float Scale, float Alpha) AnnouncementMotion(double t, double life)
    {
        if (t < Look.MotionPop)
        {
            var scale = (float)Tween.InterpolateValue(1.6, -0.6, t, Look.MotionPop, Look.PopTransition, Look.Enter).AsDouble();
            var alpha = (float)Mathf.Clamp(t / Look.MotionQuick, 0, 1);
            return (scale, alpha);
        }
        var leave = life - Look.MotionBase;
        if (t < leave)
            return (1, 1);
        var k = Mathf.Clamp((t - leave) / Look.MotionBase, 0, 1);
        var fade = (float)Tween.InterpolateValue(1.0, -1.0, k, 1, Look.EnterTransition, Look.Exit).AsDouble();
        return (1 + 0.15f * (float)k, fade);
    }

    private void Outlined(Rect2 box, string text, Font font, int size, Color fill, Color outline, HorizontalAlignment align,
        int outlineSize = 10)
    {
        var baseline = box.Position.Y + (box.Size.Y - font.GetHeight(size)) / 2 + font.GetAscent(size);
        var at = new Vector2(box.Position.X, baseline);
        DrawStringOutline(font, at, text, align, box.Size.X, size, outlineSize, outline);
        DrawString(font, at, text, align, box.Size.X, size, fill);
    }

    private static float Share(int value, int max) => max <= 0 ? 0 : Mathf.Clamp(value / (float)max, 0, 1);

    private static Rect2 Span(Rect2 inner, float fraction, bool mirrored)
    {
        var width = inner.Size.X * fraction;
        var x = mirrored ? inner.End.X - width : inner.Position.X;
        return new Rect2(x, inner.Position.Y, width, inner.Size.Y);
    }
}
