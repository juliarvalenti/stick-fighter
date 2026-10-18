using System.Collections.Generic;
using Godot;
using StickFighter.Sim.Characters;
using StickFighter.Style;

namespace StickFighter.UI;

// One slot on the character grid: a character's ink portrait, Random, or a locked slot for a
// character still to come. A focusable button, so pads move between cards with the kit's focus
// ring; each player's cursor on it is drawn in that player's color, P1 outside P2.
public partial class CharacterCard : Button
{
    private static readonly Vector2 CardSize = new(132, 168);

    public CharacterCard(Character? character, string name, bool locked = false)
    {
        ThemeTypeVariation = UiTheme.CharacterCard;
        CustomMinimumSize = CardSize;
        FocusMode = FocusModeEnum.All;
        Disabled = locked;
        Text = "";

        var column = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        column.SetAnchorsPreset(LayoutPreset.FullRect);
        column.AddThemeConstantOverride("separation", 0);
        AddChild(column);

        Control picture = character is not null
            ? new Portrait(character, Look.Ink)
            : new Label
            {
                Text = locked ? "+" : "?",
                ThemeTypeVariation = UiTheme.PaperHeading,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
        picture.SizeFlagsVertical = SizeFlags.ExpandFill;
        picture.CustomMinimumSize = new Vector2(0, 120);
        if (locked)
            picture.Modulate = new Color(1, 1, 1, 0.4f);
        column.AddChild(picture);

        var label = new Label
        {
            Text = name,
            HorizontalAlignment = HorizontalAlignment.Center,
            ThemeTypeVariation = locked ? UiTheme.PaperMuted : UiTheme.Prompt,
        };
        label.AddThemeFontSizeOverride("font_size", Look.UiSmall);
        column.AddChild(label);
    }

    // Players whose cursor is on this card (0 is P1, 1 is P2).
    public List<int> Cursors { get; } = new();

    public override void _Draw()
    {
        var rect = new Rect2(Vector2.Zero, Size);
        for (var i = 0; i < Cursors.Count; i++)
        {
            var player = Cursors[i];
            var grow = 10 - i * 8; // the first cursor outside, the second just inside it
            this.CardOutline(rect.Grow(grow), Look.Player(player), Look.BoldStroke, Look.CardRadius + grow);
            var tag = new Rect2(player == 0 ? -10 : Size.X + 10 - 52, -24, 52, 26);
            this.Pill(tag, Look.Player(player));
            this.Text(tag, $"{player + 1}P", Look.UiBold, Look.UiSmall, Look.Text);
        }
    }
}
