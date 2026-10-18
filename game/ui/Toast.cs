using Godot;
using StickFighter.Style;

namespace StickFighter.UI;

// A short notice that doesn't ask for anything ("Controller 2 connected", "Replay saved"): a dark
// pill with a colored dot for what kind of news it is. Showing and hiding it is the screen's job.
public partial class Toast : PanelContainer
{
    public enum Kind
    {
        Info,
        Success,
        Warning,
    }

    public Toast(string message, Kind kind = Kind.Info)
    {
        ThemeTypeVariation = UiTheme.Toast;
        var row = new HBoxContainer();
        AddChild(row);
        row.AddChild(new Dot(kind switch
        {
            Kind.Success => Look.Accent,
            Kind.Warning => Look.Danger,
            _ => Look.Text,
        }));
        row.AddChild(new Label { Text = message, VerticalAlignment = VerticalAlignment.Center });
    }

    private sealed partial class Dot : Control
    {
        private readonly Color _color;

        public Dot(Color color)
        {
            _color = color;
            CustomMinimumSize = new Vector2(14, 28);
        }

        public override void _Draw() => DrawCircle(Size / 2, 7, _color);
    }
}
