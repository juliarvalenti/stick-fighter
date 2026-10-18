using StickFighter.Sim.Characters;

namespace StickFighter.Sim.Combat;

// Which normal a button press starts. Built once from the character's moves, so picking a move
// in Step is an array lookup with no strings.
public static class Normals
{
    // A press this many frames old (counting the current frame) still comes out, so a button hit
    // during recovery or hitstun comes out on the first free frame.
    public const int BufferWindow = 5;

    // Heaviest first: if two buttons are pressed together, the heavier one wins.
    private static readonly Buttons[] ButtonOrder =
    {
        Buttons.HeavyKick, Buttons.HeavyPunch, Buttons.MediumKick, Buttons.MediumPunch, Buttons.LightKick, Buttons.LightPunch,
    };

    private static readonly string[] ButtonNames = { "HK", "HP", "MK", "MP", "LK", "LP" };

    // LP and LK count as pressed together when the second goes down within this many frames of the
    // first, counting the frame of the first.
    public const int ThrowLeniency = 2;

    // [standing or crouching, button in ButtonOrder] -> move index, or -1.
    private static readonly int[,] Table = Build(Characters.Characters.Placeholder);

    // The standing throw. Crouching LP+LK is crouching light kick, the heavier button, as in SF4.
    public static readonly int Throw = IndexOf(Characters.Characters.Placeholder, "LP+LK");

    // LP+LK completed on this frame: one of them went down now and the other within the leniency.
    public static bool ThrowPressedOn(in InputHistory history, int frame, int now)
    {
        var lp = history.PressedOn(frame, now).Has(Buttons.LightPunch);
        var lk = history.PressedOn(frame, now).Has(Buttons.LightKick);
        if (!lp && !lk)
            return false;
        for (var earlier = frame; earlier > frame - ThrowLeniency; earlier--)
        {
            var pressed = history.PressedOn(earlier, now);
            if ((lp && pressed.Has(Buttons.LightKick)) || (lk && pressed.Has(Buttons.LightPunch)))
                return true;
        }
        return false;
    }

    private static int[,] Build(Character character)
    {
        var table = new int[2, ButtonOrder.Length];
        for (var b = 0; b < ButtonOrder.Length; b++)
        {
            table[0, b] = IndexOf(character, "5" + ButtonNames[b]);
            table[1, b] = IndexOf(character, "2" + ButtonNames[b]);
        }
        return table;
    }

    private static int IndexOf(Character character, string input)
    {
        for (var i = 0; i < character.Moves.Length; i++)
        {
            if (character.Moves[i].Input == input)
                return i;
        }
        return -1;
    }

    // The move for these pressed buttons, standing or crouching, or -1.
    public static int Pick(Buttons pressed, bool crouching)
    {
        for (var b = 0; b < ButtonOrder.Length; b++)
        {
            if (pressed.Has(ButtonOrder[b]))
                return Table[crouching ? 1 : 0, b];
        }
        return -1;
    }
}
