using System.Collections.Generic;
using System.Linq;
using Godot;
using StickFighter.Style;
using StickFighter.UI;
using StickFighter.UI.Screens;

namespace StickFighter.Editor;

// The pose editor: open a move, scrub its frames, retime it and change its script, and see it
// recompile as you go. The move list is on the left; the move in the middle, posed with the hitbox
// view on, its timeline and playback under it, and what the checks say; its frame data (with the
// advantage it works out to) and its script verbs on the right. Every change recompiles the move
// set through MoveCompiler and runs its checks; a problem shows on the timeline and against the
// verbs of that phase, and blocks Save. Save writes the scripts and the compiled data, as the
// MoveCompiler CLI does, so `MoveCompiler --check` passes on what's saved.
//
// It edits data/ in the project, so it saves only when run from the project (not an exported game).
// The running game keeps the moves it was built with: rebuild to play an edit.
public partial class PoseEditor : Screen
{
    public const string BackItem = "Back";

    private readonly MoveDocument _doc;
    private readonly string? _saveRoot;
    private int _frame;

    private bool _playing;
    private bool _quarter;
    private bool _loop = true;
    private double _clock;

    private readonly List<Button> _moveRows = new();
    private readonly PoseView _view = new() { CustomMinimumSize = new Vector2(472, 380) };
    private readonly Timeline _timeline = new();
    private readonly Label _title = Ui.Label("", UiTheme.Strong);
    private readonly Label _where = Ui.Label("", UiTheme.Caption);
    private readonly Dictionary<string, Stepper> _numbers = new();
    private readonly Stepper _strength = new("Strength");
    private readonly Stepper _guard = new("Guard");
    private readonly Label _advantage = Ui.Label("", UiTheme.Strong);
    private readonly VBoxContainer _verbs = new();
    private readonly List<LineEdit> _fields = new();
    private readonly VBoxContainer _checks = new();
    private readonly Button _save = Ui.Button("Save");
    private readonly Label _status = Ui.Label("", UiTheme.Caption);
    private Button _play = null!;
    private Button _speed = null!;
    private Button _loopButton = null!;

    // `saveRoot` is the project folder to save into, or null where it can't be written.
    public PoseEditor(MoveDocument doc, string? saveRoot, string? input = null, int frame = 0)
    {
        _doc = doc;
        _saveRoot = saveRoot;
        if (input is not null)
            _doc.Select(input);
        _frame = frame;
    }

    // The character's move data from the project, and where to save it back (unless exported).
    public static MoveDocument Load() => new(
        FileAccess.GetFileAsString("res://data/characters/placeholder.json"),
        FileAccess.GetFileAsString("res://data/moves/library.json"),
        FileAccess.GetFileAsString("res://data/moves/placeholder.scripts.json"));

    // A verb field has focus: keys are text, and Back should only leave the field.
    public bool Typing => GetViewport().GuiGetFocusOwner() is LineEdit;

    public void StopTyping() => _timeline.GrabFocus();

    public static string? ProjectRoot => OS.HasFeature("template") ? null : ProjectSettings.GlobalizePath("res://");

    public override void _Ready()
    {
        AddChild(new Panel { ThemeTypeVariation = UiTheme.Backdrop, Size = Size });
        AddChild(MovesPanel());
        AddChild(MovePanel());
        AddChild(DataPanel());
        Refresh(rebuildVerbs: true);
        _timeline.GrabFocus();
    }

    private Control MovesPanel()
    {
        var panel = Ui.Panel();
        panel.Position = new Vector2(16, 16);
        panel.Size = new Vector2(190, 688);
        var column = new VBoxContainer();
        panel.AddChild(column);
        column.AddChild(Ui.Label("MOVES", UiTheme.Caption));
        var list = new GridContainer { Columns = 2 };
        list.AddThemeConstantOverride("h_separation", 4);
        list.AddThemeConstantOverride("v_separation", 2);
        column.AddChild(list);
        for (var i = 0; i < _doc.Inputs.Count; i++)
        {
            var index = i;
            var row = new Button
            {
                Text = _doc.Inputs[i],
                ThemeTypeVariation = UiTheme.SmallRow,
                FocusMode = FocusModeEnum.All,
                Alignment = HorizontalAlignment.Left,
                ToggleMode = true,
                CustomMinimumSize = new Vector2(69, 0),
            };
            row.Pressed += () =>
            {
                _doc.Select(index);
                _frame = 0;
                _playing = false;
                Refresh(rebuildVerbs: true);
            };
            _moveRows.Add(row);
            list.AddChild(row);
        }
        column.AddChild(new Control { SizeFlagsVertical = SizeFlags.ExpandFill });
        var back = Ui.Button(BackItem);
        back.Pressed += () => Choose(BackItem);
        column.AddChild(back);
        return panel;
    }

    private Control MovePanel()
    {
        var panel = Ui.Panel();
        panel.Position = new Vector2(222, 16);
        panel.Size = new Vector2(520, 688);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 8);
        panel.AddChild(column);
        column.AddChild(_title);
        column.AddChild(_where);
        column.AddChild(_view);
        column.AddChild(_timeline);
        _timeline.Scrubbed += frame =>
        {
            _frame = frame;
            _playing = false;
            Refresh();
        };

        var controls = new HBoxContainer();
        controls.AddThemeConstantOverride("separation", 8);
        column.AddChild(controls);
        controls.AddChild(Small("◀", () => StepFrame(-1)));
        _play = Small("Play", () =>
        {
            _playing = !_playing;
            _clock = 0;
            Refresh();
        });
        controls.AddChild(_play);
        controls.AddChild(Small("▶", () => StepFrame(1)));
        _speed = Small("1×", () =>
        {
            _quarter = !_quarter;
            Refresh();
        });
        controls.AddChild(_speed);
        _loopButton = Small("Loop", () =>
        {
            _loop = !_loop;
            Refresh();
        });
        controls.AddChild(_loopButton);

        column.AddChild(_checks);
        return panel;
    }

    private Control DataPanel()
    {
        var panel = Ui.Panel();
        panel.Position = new Vector2(758, 16);
        panel.Size = new Vector2(506, 688);
        // Tight enough that the longest script (nine verbs) fits under the frame data.
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 6);
        panel.AddChild(column);

        column.AddChild(Ui.Label("FRAME DATA", UiTheme.Caption));
        var grid = new GridContainer { Columns = 2 };
        grid.AddThemeConstantOverride("h_separation", 16);
        grid.AddThemeConstantOverride("v_separation", 0);
        column.AddChild(grid);
        foreach (var (key, label, _, _) in MoveDocument.Numbers)
        {
            var stepper = new Stepper(label) { CustomMinimumSize = new Vector2(221, 30) };
            stepper.Stepped += step =>
            {
                _doc.SetNumber(key, _doc.GetNumber(key) + step);
                Refresh();
            };
            _numbers[key] = stepper;
            grid.AddChild(stepper);
        }
        foreach (var (stepper, key) in new[] { (_strength, "strength"), (_guard, "guard") })
        {
            stepper.CustomMinimumSize = new Vector2(221, 30);
            stepper.Stepped += step =>
            {
                _doc.Cycle(key, step);
                Refresh();
            };
            grid.AddChild(stepper);
        }
        column.AddChild(_advantage);

        column.AddChild(Ui.Label("SCRIPT", UiTheme.Caption));
        _verbs.AddThemeConstantOverride("separation", 2);
        column.AddChild(_verbs);

        column.AddChild(new Control { SizeFlagsVertical = SizeFlags.ExpandFill });
        var saving = new HBoxContainer();
        saving.AddChild(_save);
        _status.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _status.VerticalAlignment = VerticalAlignment.Center;
        saving.AddChild(_status);
        column.AddChild(saving);
        _save.Pressed += Save;
        return panel;
    }

    private static Button Small(string text, System.Action pressed)
    {
        var button = Ui.Button(text);
        button.CustomMinimumSize = new Vector2(64, 40);
        button.Pressed += pressed;
        return button;
    }

    private void StepFrame(int step)
    {
        _playing = false;
        _frame += step;
        Refresh();
    }

    // Brings everything up to date with the document. The verb fields are only rebuilt when another
    // move opens, so typing in one isn't interrupted.
    private void Refresh(bool rebuildVerbs = false)
    {
        var move = _doc.Compiled;
        var frames = move?.TotalFrames ?? 1;
        _frame = Mathf.Clamp(_frame, 0, frames - 1);

        for (var i = 0; i < _moveRows.Count; i++)
            _moveRows[i].ButtonPressed = i == _doc.Selected;

        _view.Character = _doc.Result.Character ?? _doc.LastGood;
        _view.Move = move;
        _view.Frame = _frame;
        _view.QueueRedraw();

        var problems = _doc.ProblemsHere.ToList();
        _timeline.Frames = frames;
        _timeline.FirstActive = move?.FirstActiveFrame ?? 0;
        _timeline.Active = move?.Active ?? 1;
        _timeline.Flagged.Clear();
        foreach (var p in problems)
        {
            if (p.Frame is { } f)
                _timeline.Flagged.Add(f);
        }
        _timeline.Show(_frame);

        _title.Text = $"{_doc.Input}   {_doc.Name}";
        var phase = move is null ? "" : _frame < move.FirstActiveFrame ? "startup" : move.IsActive(_frame) ? "active" : "recovery";
        _where.Text = $"FRAME {_frame + 1} OF {frames}   ·   {phase.ToUpperInvariant()}";

        foreach (var (key, _, _, _) in MoveDocument.Numbers)
            _numbers[key].Show(_doc.GetNumber(key).ToString());
        _strength.Show(_doc.GetText("strength"));
        _guard.Show(_doc.GetText("guard"));
        _advantage.Text = move is null ? "" : Advantage(move);

        if (rebuildVerbs)
            BuildVerbs();
        FlagVerbs(problems, move);
        ShowChecks(problems);

        _play.Text = _playing ? "Pause" : "Play";
        _speed.Text = _quarter ? "¼×" : "1×";
        _loopButton.Text = _loop ? "Loop" : "Once";

        _save.Disabled = !_doc.CanSave || _saveRoot is null || !_doc.Changed;
        _status.Text = _saveRoot is null ? "Run from the project to save."
            : !_doc.CanSave ? "Fix the problems to save."
            : _doc.Changed ? "Unsaved changes." : "Saved data matches.";
    }

    private static string Advantage(Sim.Characters.Move move)
    {
        var (hit, block) = MoveDocument.Advantage(move);
        static string Signed(int? n, string none) => n is { } v ? (v > 0 ? $"+{v}" : v.ToString()) : none;
        return $"ON HIT {Signed(hit, "knockdown")}    ON BLOCK {Signed(block, "–")}";
    }

    private void BuildVerbs()
    {
        foreach (var child in _verbs.GetChildren())
            child.QueueFree();
        _fields.Clear();
        var verbs = _doc.Verbs;
        for (var i = 0; i < verbs.Count; i++)
        {
            var index = i;
            var field = new LineEdit { Text = verbs[i], CustomMinimumSize = new Vector2(0, 26), CaretBlink = true };
            field.TextChanged += text =>
            {
                _doc.SetVerb(index, text);
                Refresh();
            };
            _fields.Add(field);
            _verbs.AddChild(field);
        }
    }

    // A problem flags the verbs of the phase its frame is in; an error that quotes a verb flags it.
    private void FlagVerbs(List<Problem> problems, Sim.Characters.Move? move)
    {
        var flagged = new HashSet<string>();
        if (move is not null)
        {
            foreach (var p in problems)
            {
                if (p.Frame is { } f)
                    flagged.Add(f < move.FirstActiveFrame ? "startup" : move.IsActive(f) ? "active" : "recovery");
            }
        }
        var error = _doc.ErrorHere;
        var phase = "startup";
        for (var i = 0; i < _fields.Count && i < _doc.Verbs.Count; i++)
        {
            var verb = _doc.Verbs[i];
            phase = MoveDocument.PhaseOf(verb) ?? phase;
            var bad = flagged.Contains(phase) || (error is not null && error.Contains(verb));
            _fields[i].ThemeTypeVariation = bad ? UiTheme.FieldError : "";
        }
    }

    private void ShowChecks(List<Problem> problems)
    {
        foreach (var child in _checks.GetChildren())
            child.QueueFree();
        var lines = new List<string>();
        if (_doc.ErrorHere is { } error)
            lines.Add(error);
        lines.AddRange(problems.Select(p => p.Frame is { } f ? $"Frame {f + 1}: {p.Text}" : p.Text));
        var others = _doc.Result.Problems.Count - problems.Count;
        if (lines.Count == 0)
        {
            _checks.AddChild(Ui.Label(others > 0 ? $"This move passes. {others} problems in other moves." : "All checks pass.", UiTheme.Caption));
            return;
        }
        foreach (var line in lines.Take(3))
        {
            var label = Ui.Label(line, UiTheme.Strong);
            label.AddThemeColorOverride("font_color", Look.LowHealth);
            label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            label.CustomMinimumSize = new Vector2(472, 0);
            _checks.AddChild(label);
        }
        if (lines.Count > 3)
            _checks.AddChild(Ui.Label($"and {lines.Count - 3} more", UiTheme.Caption));
    }

    private void Save()
    {
        if (_saveRoot is null || _doc.SaveFiles() is not { } files)
            return;
        foreach (var (path, text) in files)
        {
            using var file = FileAccess.Open(System.IO.Path.Combine(_saveRoot, path), FileAccess.ModeFlags.Write);
            if (file is null)
            {
                _status.Text = $"Couldn't write {path}: {FileAccess.GetOpenError()}";
                return;
            }
            file.StoreString(text);
        }
        _doc.Saved();
        Refresh();
        _status.Text = "Saved. Rebuild to play it in the game.";
    }

    public override void _Process(double delta)
    {
        if (!_playing || _doc.Compiled is not { } move)
            return;
        _clock += delta * 60 * (_quarter ? 0.25 : 1);
        var advanced = false;
        while (_clock >= 1)
        {
            _clock -= 1;
            advanced = true;
            if (_frame + 1 < move.TotalFrames)
            {
                _frame++;
            }
            else if (_loop)
            {
                _frame = 0;
            }
            else
            {
                _playing = false;
                break;
            }
        }
        if (advanced)
            Refresh();
    }
}
