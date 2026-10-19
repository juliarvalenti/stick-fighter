using StickFighter.Sim.Characters;

namespace StickFighter.Sim.Combat;

// Cancels and chains, SF4's: a normal that connects, on hit or on block (never on a whiff), can have
// the rest of its frames cut short by a special, if the move is special-cancelable, or by one of the
// normals in its chain table (lights into lights). Only ever forwards: normal into special (into
// super, once there are supers), never a special into anything; the loader holds the data to that.
//
// The window opens on contact and runs through the hitstop (nothing acts while frozen, and the buffer
// keeps what was pressed then) and a few frames after it. Action frames don't advance in hitstop, so
// counting them from the contact counts only unfrozen frames.
public static class Cancels
{
    // [from move, to move]: the first can chain into the second.
    private static readonly bool[,] ChainTable = Build(Characters.Characters.Placeholder);

    // The fighter's move connected, and no more than `window` unfrozen frames ago.
    public static bool Open(in FighterState fighter, int window) =>
        fighter.Action == (int)FighterAction.Attack && fighter.Move >= 0 && fighter.MoveHit
        && fighter.ActionFrame - fighter.ContactFrame < window;

    public static bool SpecialCancelable(in FighterState fighter, int window) =>
        Open(fighter, window) && Characters.Characters.Placeholder.Moves[fighter.Move].SpecialCancel;

    public static bool Chains(int from, int to) => from >= 0 && to >= 0 && ChainTable[from, to];

    private static bool[,] Build(Character character)
    {
        var moves = character.Moves;
        var table = new bool[moves.Length, moves.Length];
        for (var from = 0; from < moves.Length; from++)
        {
            foreach (var input in moves[from].Chains)
            {
                for (var to = 0; to < moves.Length; to++)
                {
                    if (moves[to].Input == input)
                        table[from, to] = true;
                }
            }
        }
        return table;
    }
}
