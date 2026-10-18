namespace StickFighter.Sim;

// Both players' buttons for one frame. A placeholder until the input layer task
// defines the real encoding; the simulation only ever sees this, never a device.
public struct FrameInput
{
    public const ushort Left = 1 << 0;
    public const ushort Right = 1 << 1;

    public ushort Player1;
    public ushort Player2;

    public FrameInput(ushort player1, ushort player2)
    {
        Player1 = player1;
        Player2 = player2;
    }

    public readonly ushort For(int player) => player == 0 ? Player1 : Player2;
}
