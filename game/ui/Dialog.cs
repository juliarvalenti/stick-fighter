using Godot;

namespace StickFighter.UI;

// A question with a few answers: a dark card with a heading, a line of text and a row of buttons.
// The first button takes focus when the dialog opens, so a pad can answer straight away.
public partial class Dialog : PanelContainer
{
    public Dialog(string title, string body, params string[] answers)
    {
        ThemeTypeVariation = UiTheme.Dialog;
        CustomMinimumSize = new Vector2(480, 0);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 16);
        AddChild(column);

        column.AddChild(new Label { Text = title, ThemeTypeVariation = UiTheme.Heading });
        // A wrapping label needs a width to wrap to, or its minimum height runs away.
        column.AddChild(new Label { Text = body, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(416, 0) });

        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        column.AddChild(buttons);
        foreach (var answer in answers)
        {
            var button = Ui.Button(answer);
            buttons.AddChild(button);
            Buttons.Add(button);
        }
    }

    public System.Collections.Generic.List<Button> Buttons { get; } = new();

    public override void _Ready()
    {
        if (Buttons.Count > 0)
            Buttons[0].CallDeferred(Control.MethodName.GrabFocus);
    }
}
