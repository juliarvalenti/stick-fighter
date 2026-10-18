using Godot;
using StickFighter.Sim.Characters;

namespace StickFighter.UI.Screens;

// Pause, over the match dimmed and frozen behind it: resume first (focused), then the move list,
// the command list, options and quit. The move list page shows every move the character has as
// its notation's glyphs, from the same data the game plays.
public partial class PauseScreen : Screen
{
    private readonly bool _moveList;

    public PauseScreen(bool moveList = false) => _moveList = moveList;

    public override void _Ready()
    {
        AddChild(new Panel { ThemeTypeVariation = UiTheme.ScrimPanel, Size = Size });
        if (_moveList)
            BuildMoveList();
        else
            BuildMenu();
    }

    private void BuildMenu()
    {
        var panel = Ui.Panel();
        panel.CustomMinimumSize = new Vector2(360, 0);
        var column = new VBoxContainer();
        panel.AddChild(column);
        column.AddChild(new Label { Text = "PAUSED", ThemeTypeVariation = UiTheme.Heading, HorizontalAlignment = HorizontalAlignment.Center });
        var first = Menu(column, "Resume", "Move list", "Command list", "Options", "Quit to menu");
        AddChild(panel);
        CenterAt(panel, 120);
        first.GrabFocus();
    }

    private void BuildMoveList()
    {
        var panel = Ui.Panel();
        var column = new VBoxContainer();
        panel.AddChild(column);
        column.AddChild(Ui.Heading("MOVE LIST"));
        column.AddChild(Ui.Label("STICK", UiTheme.Caption));

        var grid = new GridContainer { Columns = 6 };
        grid.AddThemeConstantOverride("h_separation", 16);
        grid.AddThemeConstantOverride("v_separation", 8);
        column.AddChild(grid);
        foreach (var move in Characters.Placeholder.Moves)
        {
            grid.AddChild(new Label { Text = move.Name, CustomMinimumSize = new Vector2(196, 0), VerticalAlignment = VerticalAlignment.Center });
            grid.AddChild(Glyphs.For(move.Input, 30, onPanel: true));
            grid.AddChild(new Control { CustomMinimumSize = new Vector2(24, 0) });
        }
        AddChild(panel);
        CenterAt(panel, 40);
    }
}
