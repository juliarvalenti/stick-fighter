using System.Collections.Generic;
using StickFighter.Sim;

namespace StickFighter.Training;

// One row of the input display: a direction (numpad, relative to facing), the attack buttons
// held, from which frame, and for how many frames.
public readonly record struct InputRow(int Direction, Buttons Attacks, int Start, int Frames);

// A player's input log, SF-style: a new row whenever the direction or the held buttons change,
// otherwise the current row's frame count grows. Fed one frame at a time from the cleaned input
// the simulation recorded (GameState.Inputs), so it shows exactly what the game read.
//
// Plain C#, so it's tested without Godot. Keyed by frame: told about a frame it has already seen
// (a rollback resimulating), it drops what came after and carries on from there.
public sealed class InputLog
{
    public const int MaxRows = 24;

    private readonly List<InputRow> _rows = new();
    private int _last = -1;

    // Newest last.
    public IReadOnlyList<InputRow> Rows => _rows;

    public void Observe(int frame, Buttons cleaned, int facing)
    {
        if (frame <= _last)
            Rewind(frame);
        _last = frame;

        var direction = Numpad.Direction(cleaned, facing);
        var attacks = cleaned & Buttons.Attacks;
        if (_rows.Count > 0 && _rows[^1] is var row && row.Direction == direction && row.Attacks == attacks)
        {
            _rows[^1] = row with { Frames = row.Frames + 1 };
            return;
        }
        _rows.Add(new InputRow(direction, attacks, frame, 1));
        if (_rows.Count > MaxRows)
            _rows.RemoveAt(0);
    }

    // Reads every frame the sim's history still holds, up to `last` (its newest frame). Frames
    // already logged are read again, so a rollback that changed them corrects the log.
    public void Read(in InputHistory history, int last, int facing)
    {
        for (var frame = System.Math.Max(0, last - InputHistory.MaxWindow); frame <= last; frame++)
            Observe(frame, history.At(frame, last), facing);
    }

    // Forgets `frame` and everything after it.
    private void Rewind(int frame)
    {
        while (_rows.Count > 0 && _rows[^1].Start >= frame)
            _rows.RemoveAt(_rows.Count - 1);
        if (_rows.Count > 0 && _rows[^1] is var row && row.Start + row.Frames > frame)
            _rows[^1] = row with { Frames = frame - row.Start };
    }

    // The row's notation for the glyphs: "6", "2LP", "4MP+LP" (buttons in LP MP HP LK MK HK order).
    public static string Notation(InputRow row)
    {
        var text = row.Direction.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var first = true;
        foreach (var (button, name) in Names)
        {
            if (!row.Attacks.Has(button))
                continue;
            text += (first ? "" : "+") + name;
            first = false;
        }
        return text;
    }

    // The row as glyphs: its direction, then each button held, joined by "+".
    public static IReadOnlyList<UI.Glyph> Glyphs(InputRow row)
    {
        var glyphs = new List<UI.Glyph> { new(UI.GlyphKind.Direction, row.Direction) };
        var first = true;
        foreach (var (button, attack, strength) in Kinds)
        {
            if (!row.Attacks.Has(button))
                continue;
            if (!first)
                glyphs.Add(new UI.Glyph(UI.GlyphKind.Plus));
            glyphs.Add(new UI.Glyph(UI.GlyphKind.Button, Attack: attack, Strength: strength));
            first = false;
        }
        return glyphs;
    }

    private static readonly (Buttons Button, UI.Attack Attack, UI.Strength Strength)[] Kinds =
    {
        (Buttons.LightPunch, UI.Attack.Punch, UI.Strength.Light), (Buttons.MediumPunch, UI.Attack.Punch, UI.Strength.Medium),
        (Buttons.HeavyPunch, UI.Attack.Punch, UI.Strength.Heavy), (Buttons.LightKick, UI.Attack.Kick, UI.Strength.Light),
        (Buttons.MediumKick, UI.Attack.Kick, UI.Strength.Medium), (Buttons.HeavyKick, UI.Attack.Kick, UI.Strength.Heavy),
    };

    private static readonly (Buttons Button, string Name)[] Names =
    {
        (Buttons.LightPunch, "LP"), (Buttons.MediumPunch, "MP"), (Buttons.HeavyPunch, "HP"),
        (Buttons.LightKick, "LK"), (Buttons.MediumKick, "MK"), (Buttons.HeavyKick, "HK"),
    };
}
