using System.Collections.Generic;
using Godot;
using StickFighter.Settings;
using StickFighter.Style;

namespace StickFighter.UI.Screens;

// One player's controls, keyboard or pad (the tabs): every binding with what it's on now. Choosing
// one waits for the next key or pad input and binds it there. An input this player already uses
// swaps with it; a key the other player uses is refused with a warning (they share the keyboard).
// Escape (or Start on a pad) stops waiting without changing anything. Reset puts this player's
// keyboard and pad back to the defaults. Every change raises Changed, to be saved.
public partial class RebindScreen : Screen
{
    public const string Reset = "Reset to defaults";

    private static readonly Dictionary<Binding, string> Names = new()
    {
        [Binding.Up] = "Up",
        [Binding.Down] = "Down",
        [Binding.Left] = "Back / left",
        [Binding.Right] = "Forward / right",
        [Binding.LP] = "Light punch",
        [Binding.MP] = "Medium punch",
        [Binding.HP] = "Heavy punch",
        [Binding.LK] = "Light kick",
        [Binding.MK] = "Medium kick",
        [Binding.HK] = "Heavy kick",
    };

    private const float PadAxisPress = 0.6f;

    private readonly int _player;
    private readonly UserSettings _settings;
    private Device _device;
    private Binding? _waiting;

    private readonly Dictionary<Binding, Button> _rows = new();
    private readonly Dictionary<Binding, Label> _values = new();
    private Label _prompt = null!;
    private VBoxContainer _toasts = null!;

    public enum Device
    {
        Keyboard,
        Pad,
    }

    public RebindScreen(int player, UserSettings settings, Device device = Device.Keyboard)
    {
        _player = player;
        _settings = settings;
        _device = device;
    }

    public event System.Action? Changed;

    // Waiting for an input, so Back shouldn't leave the screen (Escape is what cancels it).
    public bool Waiting => _waiting is not null;

    private Bindings Bindings => _settings.Bindings;

    public override void _Ready()
    {
        AddChild(new Panel { ThemeTypeVariation = UiTheme.ScrimPanel, Size = Size });

        var panel = Ui.Panel();
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 12);
        panel.AddChild(column);

        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", 24);
        head.AddChild(new PlayerTag(_player));
        head.AddChild(Ui.Heading("CONTROLS"));
        column.AddChild(head);

        var tabs = Ui.Tabs((int)_device, "Keyboard", "Pad");
        tabs.TabChanged += tab =>
        {
            _device = (Device)(int)tab;
            _waiting = null;
            Refresh();
        };
        column.AddChild(tabs);

        var sides = new HBoxContainer();
        sides.AddThemeConstantOverride("separation", 40);
        column.AddChild(sides);
        var moves = new VBoxContainer();
        var attacks = new VBoxContainer();
        sides.AddChild(moves);
        sides.AddChild(attacks);
        moves.AddChild(Ui.Label("MOVE", UiTheme.Caption));
        attacks.AddChild(Ui.Label("ATTACK", UiTheme.Caption));
        var order = new List<Control>();
        foreach (var binding in Bindings.All)
        {
            var line = BindingLine(binding);
            (binding <= Binding.Right ? moves : attacks).AddChild(line);
            order.Add(_rows[binding]);
        }

        var footer = new HBoxContainer();
        footer.AddThemeConstantOverride("separation", 16);
        column.AddChild(footer);
        var reset = Ui.Row(Reset);
        reset.Pressed += () =>
        {
            Bindings.ResetToDefaults(_player);
            _waiting = null;
            Refresh();
            Changed?.Invoke();
        };
        footer.AddChild(reset);
        order.Add(reset);

        _prompt = Ui.Label("", UiTheme.Caption);
        column.AddChild(_prompt);

        AddChild(panel);
        CenterAt(panel, 56);

        _toasts = new VBoxContainer { Position = new Vector2(0, 600), Size = new Vector2(1280, 0) };
        AddChild(_toasts);

        // Down through the moves, then the attacks, then reset; up from the first row is the tabs.
        for (var i = 0; i < order.Count; i++)
        {
            order[i].FocusNeighborBottom = order[i].GetPathTo(order[(i + 1) % order.Count]);
            order[i].FocusNeighborTop = i == 0 ? order[i].GetPathTo(tabs) : order[i].GetPathTo(order[i - 1]);
        }
        tabs.FocusNeighborBottom = tabs.GetPathTo(order[0]);

        Refresh();
        order[0].GrabFocus();
    }

    private Control BindingLine(Binding binding)
    {
        var row = Ui.Row(Names[binding]);
        row.CustomMinimumSize = new Vector2(250, 48);
        row.Alignment = HorizontalAlignment.Left;
        row.Pressed += () =>
        {
            _waiting = binding;
            Refresh();
        };
        _rows[binding] = row;

        var value = Ui.Label("", UiTheme.Chip);
        value.CustomMinimumSize = new Vector2(150, 0);
        value.HorizontalAlignment = HorizontalAlignment.Center;
        value.VerticalAlignment = VerticalAlignment.Center;
        value.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        _values[binding] = value;

        var line = new HBoxContainer();
        line.AddThemeConstantOverride("separation", 12);
        line.AddChild(row);
        line.AddChild(value);
        return line;
    }

    private void Refresh()
    {
        foreach (var binding in Bindings.All)
        {
            var waiting = binding == _waiting;
            _values[binding].Text = waiting ? "PRESS…" : Describe(binding);
            _values[binding].ThemeTypeVariation = waiting ? UiTheme.ChipOn : UiTheme.Chip;
        }
        _prompt.Text = _waiting is { } b
            ? _device == Device.Keyboard
                ? $"Press a key for {Names[b].ToLowerInvariant()}. Escape to cancel."
                : $"Press a button for {Names[b].ToLowerInvariant()}. Start to cancel."
            : "Choose a control to change it. Back to return.";
    }

    private string Describe(Binding binding) => _device == Device.Keyboard
        ? Bindings.Key(_player, binding) is var key and not 0 ? KeyName(key) : "—"
        : Bindings.Pad(_player, binding) is { } pad ? PadName(pad) : "—";

    public static string KeyName(long key) => OS.GetKeycodeString((Key)key).ToUpperInvariant();

    public static string PadName(PadInput pad)
    {
        if (pad.Axis)
        {
            return (JoyAxis)pad.Index switch
            {
                JoyAxis.TriggerLeft => "LT",
                JoyAxis.TriggerRight => "RT",
                JoyAxis.LeftX => pad.Sign < 0 ? "L-STICK ←" : "L-STICK →",
                JoyAxis.LeftY => pad.Sign < 0 ? "L-STICK ↑" : "L-STICK ↓",
                JoyAxis.RightX => pad.Sign < 0 ? "R-STICK ←" : "R-STICK →",
                JoyAxis.RightY => pad.Sign < 0 ? "R-STICK ↑" : "R-STICK ↓",
                _ => $"AXIS {pad.Index}",
            };
        }
        return (JoyButton)pad.Index switch
        {
            JoyButton.A => "A",
            JoyButton.B => "B",
            JoyButton.X => "X",
            JoyButton.Y => "Y",
            JoyButton.Back => "BACK",
            JoyButton.Start => "START",
            JoyButton.LeftStick => "L3",
            JoyButton.RightStick => "R3",
            JoyButton.LeftShoulder => "LB",
            JoyButton.RightShoulder => "RB",
            JoyButton.DpadUp => "D-PAD ↑",
            JoyButton.DpadDown => "D-PAD ↓",
            JoyButton.DpadLeft => "D-PAD ←",
            JoyButton.DpadRight => "D-PAD →",
            _ => $"BUTTON {pad.Index}",
        };
    }

    // While waiting, the next key (keyboard tab) or pad input (pad tab) is the binding, before the
    // menus see it.
    public override void _Input(InputEvent @event)
    {
        if (_waiting is not { } binding)
            return;
        if (_device == Device.Keyboard && @event is InputEventKey { Pressed: true, Echo: false } key)
        {
            GetViewport().SetInputAsHandled();
            if (key.PhysicalKeycode == Godot.Key.Escape)
                Stop();
            else
                Bound(binding, Bindings.BindKey(_player, binding, (long)key.PhysicalKeycode));
        }
        else if (_device == Device.Pad && @event is InputEventJoypadButton { Pressed: true } button)
        {
            GetViewport().SetInputAsHandled();
            if (button.ButtonIndex == JoyButton.Start)
                Stop();
            else
                Bound(binding, Bindings.BindPad(_player, binding, PadInput.Button((int)button.ButtonIndex)));
        }
        else if (_device == Device.Pad && @event is InputEventJoypadMotion motion
            && motion.Axis is not (JoyAxis.LeftX or JoyAxis.LeftY) && Mathf.Abs(motion.AxisValue) > PadAxisPress)
        {
            // The left stick always moves, so it isn't bound; triggers and the right stick can be.
            GetViewport().SetInputAsHandled();
            Bound(binding, Bindings.BindPad(_player, binding, PadInput.Stick((int)motion.Axis, Mathf.Sign(motion.AxisValue))));
        }
    }

    private void Stop()
    {
        _waiting = null;
        Refresh();
    }

    private void Bound(Binding binding, BindResult result)
    {
        switch (result)
        {
            case BindResult.Swapped:
                // Whatever had it now has this binding's old input (its label still shows the old one).
                foreach (var other in Bindings.All)
                {
                    if (other != binding && _values[other].Text != Describe(other))
                        Notify($"Swapped: {Names[other].ToLowerInvariant()} is now on {Describe(other)}", Toast.Kind.Info);
                }
                break;
            case BindResult.TakenByOtherPlayer:
                Notify($"Player {2 - _player} uses that key. Pick another.", Toast.Kind.Warning);
                return; // still waiting
        }
        _waiting = null;
        Refresh();
        Changed?.Invoke();
    }

    private void Notify(string message, Toast.Kind kind)
    {
        foreach (var child in _toasts.GetChildren())
            child.QueueFree();
        _toasts.AddChild(new Toast(message, kind) { SizeFlagsHorizontal = SizeFlags.ShrinkCenter });
    }

    // "P1" in the player's color, as the HUD's name tag.
    private sealed partial class PlayerTag : Control
    {
        private readonly int _player;

        public PlayerTag(int player)
        {
            _player = player;
            CustomMinimumSize = new Vector2(72, 44);
            SizeFlagsVertical = SizeFlags.ShrinkCenter;
        }

        public override void _Draw()
        {
            var box = new Rect2(Vector2.Zero, Size);
            this.Pill(box, Look.Player(_player));
            this.Text(box, $"P{_player + 1}", Look.UiBold, Look.UiMedium, Look.Text);
        }
    }
}
