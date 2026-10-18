using Godot;

namespace StickFighter.UI.Screens;

// The title: the game's name over a fight that loops behind it (the scenario drives the fighters),
// then, once start is pressed, the main menu on a panel with its first row focused.
public partial class TitleScreen : Screen
{
    private readonly bool _menu;

    public TitleScreen(bool menu = false) => _menu = menu;

    public override void _Ready()
    {
        var head = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        head.AddThemeConstantOverride("separation", 0);
        head.AddChild(new Label { Text = "STICK FIGHTER", ThemeTypeVariation = UiTheme.Title, HorizontalAlignment = HorizontalAlignment.Center });
        var rule = new PlayerRule { SizeFlagsHorizontal = SizeFlags.ShrinkCenter };
        head.AddChild(rule);
        AddChild(head);
        CenterAt(head, 48);

        if (!_menu)
        {
            var prompt = new Label { Text = "PRESS START", ThemeTypeVariation = UiTheme.Prompt };
            AddChild(prompt);
            CenterAt(prompt, 624);
        }
        else
        {
            var panel = Ui.Panel();
            var rows = new VBoxContainer();
            panel.AddChild(rows);
            var first = Menu(rows, "Versus", "Training", "Options", "Quit");
            AddChild(panel);
            CenterAt(panel, 244);
            first.GrabFocus();
        }

        var version = new Label { Text = "v0.1", ThemeTypeVariation = UiTheme.PaperMuted, Position = new Vector2(1180, 676) };
        AddChild(version);
    }
}
