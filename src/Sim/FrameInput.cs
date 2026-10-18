namespace StickFighter.Sim;

// Both players' raw input for one frame: everything the simulation learns from the outside world.
// The input layer in game/ builds it from devices; later the rollback layer builds it from the network.
public struct FrameInput
{
    public Buttons Player1;
    public Buttons Player2;

    public FrameInput(Buttons player1, Buttons player2)
    {
        Player1 = player1;
        Player2 = player2;
    }

    public readonly Buttons For(int player) => player == 0 ? Player1 : Player2;
}
