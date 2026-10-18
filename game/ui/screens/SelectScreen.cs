using System;
using Godot;
using StickFighter.Sim.Characters;
using StickFighter.Style;

namespace StickFighter.UI.Screens;

// Character select. A grid of cards (the one character, Random, and a locked slot for the second
// character), each player's cursor in their color, and under it each player's panel: their pick
// as a portrait in their color, the color choices, and whether they've locked in.
//
// Players pick in turn, P1 then P2, with the menu controls: move between cards, confirm to lock in,
// the color key (C, or the pad's Y) to switch to the alternate, back to undo P1's pick or leave.
// Mirror match: when both pick the same character, the second picker starts on their alternate
// color, so the two fighters never read alike. Random picks among the characters there are.
public partial class SelectScreen : Screen
{
    // The shared action for switching color, added to the input map if it isn't there.
    public const string ColorAction = "change_color";

    private const int Cards = 3; // Stick, Random, locked
    private const int RandomCard = 1;

    private readonly int[] _picked = { -1, -1 }; // the card each player locked in on
    private readonly int[] _color = { 0, 0 };
    private int _turn; // whose pick it is; 2 when both have picked
    private int _focus;
    private readonly ulong _seed;

    private CharacterCard[] _cards = Array.Empty<CharacterCard>();
    private Control? _panels;

    // `interactive` false shows a fixed moment (P1 locked in, P2 choosing on Stick in a mirror), for
    // the kit's screen scenario.
    public SelectScreen(bool interactive = false, ulong seed = 1)
    {
        _seed = seed;
        if (!interactive)
        {
            _picked[0] = 0;
            _turn = 1;
            _color[1] = 1;
            Interactive = false;
        }
    }

    public bool Interactive { get; } = true;

    // Both players have picked: the colors they chose.
    public event Action<int, int>? Done;

    // Back from P1's turn: leave select.
    public event Action? Back;

    public override void _Ready()
    {
        EnsureColorAction();
        AddChild(new Panel { ThemeTypeVariation = UiTheme.Backdrop, Size = Size });

        var heading = new Label { Text = "CHOOSE YOUR FIGHTER", ThemeTypeVariation = UiTheme.PaperHeading };
        AddChild(heading);
        CenterAt(heading, 16);

        var character = Characters.Placeholder;
        var grid = new HBoxContainer();
        grid.AddThemeConstantOverride("separation", 28);
        _cards = new[]
        {
            new CharacterCard(character, "STICK"),
            new CharacterCard(null, "RANDOM"),
            new CharacterCard(null, "SOON", locked: true),
        };
        for (var i = 0; i < Cards; i++)
        {
            var index = i;
            grid.AddChild(_cards[i]);
            _cards[i].FocusEntered += () =>
            {
                _focus = index;
                Refresh();
            };
            _cards[i].Pressed += () => Pick(index);
        }
        AddChild(grid);
        CenterAt(grid, 120);

        var hint = new Label
        {
            Text = "Confirm to lock in   ·   C or Y to change color   ·   Back to undo",
            ThemeTypeVariation = UiTheme.PaperMuted,
        };
        AddChild(hint);
        CenterAt(hint, 684);

        _cards[0].GrabFocus();
        Refresh();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!Interactive || _turn > 1)
            return;
        if (@event.IsActionPressed(ColorAction))
        {
            _color[_turn] = 1 - _color[_turn];
            Refresh();
            GetViewport().SetInputAsHandled();
        }
        else if (@event.IsActionPressed("ui_cancel"))
        {
            GetViewport().SetInputAsHandled();
            if (_turn == 1)
            {
                _turn = 0;
                _picked[0] = -1;
                _color[1] = 0;
                _cards[0].GrabFocus();
                Refresh();
            }
            else
            {
                Back?.Invoke();
            }
        }
    }

    private void Pick(int card)
    {
        if (!Interactive || _turn > 1)
            return;
        _picked[_turn] = card == RandomCard ? RandomCharacterCard() : card;
        _turn++;
        if (_turn == 1)
        {
            // The second picker starts on their alternate if they're about to mirror P1.
            _color[1] = _picked[0] == 0 ? 1 : 0;
            _cards[0].GrabFocus();
            Refresh();
            return;
        }
        Refresh();
        Done?.Invoke(_color[0], _color[1]);
    }

    // The cards that are real characters (one so far: Stick).
    private static readonly int[] CharacterCards = { 0 };

    // Random lands on a real character, from the match seed, so a match is reproducible.
    private int RandomCharacterCard() => CharacterCards[(int)(_seed % (ulong)CharacterCards.Length)];

    private void Refresh()
    {
        foreach (var card in _cards)
            card.Cursors.Clear();
        for (var p = 0; p < 2; p++)
        {
            var on = p < _turn ? _picked[p] : p == _turn ? _focus : -1;
            if (on >= 0)
                _cards[on].Cursors.Add(p);
        }
        foreach (var card in _cards)
            card.QueueRedraw();

        _panels?.QueueFree();
        _panels = new Control { MouseFilter = MouseFilterEnum.Ignore, Size = Size };
        var character = Characters.Placeholder;
        var mirror = _turn >= 1 && (_turn > 1 ? _picked[1] : Hovered(1)) == _picked[0];
        _panels.AddChild(PlayerPanel(0, character, new[] { Look.P1, Look.P1Alt }, _color[0], ready: _turn > 0, note: null, x: 48));
        _panels.AddChild(PlayerPanel(1, character, new[] { Look.P2, Look.P2Alt }, _color[1], ready: _turn > 1,
            note: mirror && _color[1] == 1 ? "Mirror match: alternate color" : null, x: 1280 - 48 - 400));
        AddChild(_panels);
    }

    // The card a player's cursor is on while they choose (Random shows as the character it'll be).
    private int Hovered(int player) => player == _turn ? (_focus == RandomCard ? RandomCharacterCard() : _focus) : _picked[player];

    private static Control PlayerPanel(int player, Character character, Color[] colors, int chosen, bool ready, string? note, float x)
    {
        var panel = Ui.Panel();
        panel.CustomMinimumSize = new Vector2(400, 0);
        panel.Position = new Vector2(x, 330);

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 20);
        panel.AddChild(row);
        row.AddChild(new Portrait(character, colors[chosen], facingLeft: player == 1) { CustomMinimumSize = new Vector2(120, 240) });

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 10);
        row.AddChild(column);
        column.AddChild(Ui.Label($"PLAYER {player + 1}", UiTheme.Caption));
        column.AddChild(Ui.Heading("STICK"));
        column.AddChild(new ColorPick(colors, chosen));
        column.AddChild(new Label { Text = ready ? "READY" : "CHOOSING", ThemeTypeVariation = ready ? UiTheme.ChipOn : UiTheme.Chip, SizeFlagsHorizontal = SizeFlags.ShrinkBegin });
        if (note is not null)
            column.AddChild(Ui.Label(note, UiTheme.Caption));
        return panel;
    }

    // C on the keyboard, the top face button on a pad (Y on an Xbox layout).
    public static void EnsureColorAction()
    {
        if (InputMap.HasAction(ColorAction))
            return;
        InputMap.AddAction(ColorAction);
        InputMap.ActionAddEvent(ColorAction, new InputEventKey { PhysicalKeycode = Key.C });
        InputMap.ActionAddEvent(ColorAction, new InputEventJoypadButton { ButtonIndex = JoyButton.Y });
    }
}
