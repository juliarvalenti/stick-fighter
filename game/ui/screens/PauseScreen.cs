using System.Linq;
using Godot;
using StickFighter.Sim.Characters;

namespace StickFighter.UI.Screens;

// Pause, over the match dimmed and frozen behind it: resume first (focused), then the move list,
// the command list, options and quit. Each page is its own screen; whoever shows them (Main) moves
// between them on Chosen, and back on the cancel action.
//
// The move list shows the moves a player can do now (Menus.Triggerable), as their notation's glyphs,
// from the same data the game plays. The command list is the system controls: walking, dashing,
// jumping, guarding and throwing.
public partial class PauseScreen : Screen
{
    public enum Page
    {
        Menu,
        MoveList,
        Commands,
        ConfirmQuit,
    }

    public const string Resume = "Resume";
    public const string MoveListItem = "Move list";
    public const string CommandListItem = "Command list";
    public const string Options = "Options";
    public const string Quit = "Quit to menu";
    public const string Stay = "Stay";
    public const string QuitConfirmed = "Quit";

    private readonly Page _page;
    private readonly string _focus;

    // `focus` is the menu row to land on, so coming back from a page returns to where it was.
    public PauseScreen(Page page = Page.Menu, string focus = Resume)
    {
        _page = page;
        _focus = focus;
    }

    // The old way to ask for the move list page, kept for the kit's screen scenarios.
    public PauseScreen(bool moveList)
        : this(moveList ? Page.MoveList : Page.Menu)
    {
    }

    public Page Showing => _page;

    public override void _Ready()
    {
        AddChild(new Panel { ThemeTypeVariation = UiTheme.ScrimPanel, Size = Size });
        switch (_page)
        {
            case Page.MoveList:
                BuildMoveList();
                break;
            case Page.Commands:
                BuildCommands();
                break;
            default:
                BuildMenu();
                break;
        }
    }

    private void BuildMenu()
    {
        var panel = Ui.Panel();
        panel.CustomMinimumSize = new Vector2(360, 0);
        var column = new VBoxContainer();
        panel.AddChild(column);
        column.AddChild(new Label { Text = "PAUSED", ThemeTypeVariation = UiTheme.Heading, HorizontalAlignment = HorizontalAlignment.Center });
        Menu(column, Resume, MoveListItem, CommandListItem, Options, Quit);
        AddChild(panel);
        CenterAt(panel, 120);

        // Options arrive with the options task; until then the row shows but can't be chosen.
        var rows = column.GetChildren().OfType<Button>().ToList();
        foreach (var row in rows.Where(r => r.Text == Options))
            row.Disabled = true;

        if (_page == Page.ConfirmQuit)
        {
            // The kit's dialog over the menu, the safe answer focused.
            var dialog = new Dialog("Quit to menu?", "The match so far will be lost.", Stay, QuitConfirmed);
            AddChild(dialog);
            dialog.Position = (Size - dialog.GetCombinedMinimumSize()) / 2;
            foreach (var button in dialog.Buttons)
            {
                var answer = button.Text;
                button.Pressed += () => Choose(answer);
            }
            return;
        }
        (rows.FirstOrDefault(r => r.Text == _focus) ?? rows[0]).GrabFocus();
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
        var moves = Characters.Placeholder.Moves;
        for (var i = 0; i < moves.Length; i++)
        {
            if (!Menus.Triggerable.Moves.Contains(i))
                continue;
            grid.AddChild(new Label { Text = moves[i].Name, CustomMinimumSize = new Vector2(196, 0), VerticalAlignment = VerticalAlignment.Center });
            grid.AddChild(Glyphs.For(moves[i].Input, 30, onPanel: true));
            grid.AddChild(new Control { CustomMinimumSize = new Vector2(24, 0) });
        }
        column.AddChild(Ui.Label("Back to return", UiTheme.Caption));
        AddChild(panel);
        CenterAt(panel, 40);
    }

    // The system controls, as glyphs: facing right, 6 is toward the opponent.
    private static readonly (string Name, Glyph[] Glyphs)[] Commands =
    {
        ("Walk", new[] { Dir(4), Dir(6) }),
        ("Crouch", new[] { Dir(2) }),
        ("Jump (back, up, forward)", new[] { Dir(7), Dir(8), Dir(9) }),
        ("Dash", new[] { Dir(6), Dir(6) }),
        ("Back dash", new[] { Dir(4), Dir(4) }),
        ("Guard high and mid", new[] { Dir(4) }),
        ("Guard low and mid", new[] { Dir(1) }),
        ("Throw", Notation.Parse("LP+LK").ToArray()),
        ("Back throw", new[] { Dir(4) }.Concat(Notation.Parse("LP+LK")).ToArray()),
    };

    private static Glyph Dir(int direction) => new(GlyphKind.Direction, direction);

    private void BuildCommands()
    {
        var panel = Ui.Panel();
        panel.CustomMinimumSize = new Vector2(560, 0);
        var column = new VBoxContainer();
        panel.AddChild(column);
        column.AddChild(Ui.Heading("COMMAND LIST"));
        var grid = new GridContainer { Columns = 2 };
        grid.AddThemeConstantOverride("h_separation", 24);
        grid.AddThemeConstantOverride("v_separation", 8);
        column.AddChild(grid);
        foreach (var (name, glyphs) in Commands)
        {
            grid.AddChild(new Label { Text = name, CustomMinimumSize = new Vector2(260, 0), VerticalAlignment = VerticalAlignment.Center });
            grid.AddChild(Glyphs.For(glyphs, 30, onPanel: true));
        }
        column.AddChild(Ui.Label("Back to return", UiTheme.Caption));
        AddChild(panel);
        CenterAt(panel, 60);
    }
}
