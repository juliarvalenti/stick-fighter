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

    // [standing or crouching, button in ButtonOrder] -> move index, or -1.
    private static readonly int[,] Table = Build(Characters.Characters.Placeholder);

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
