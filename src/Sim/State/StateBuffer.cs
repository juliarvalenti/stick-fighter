namespace StickFighter.Sim;

// Saved states for the last Capacity frames, for rollback. Preallocated; saving and loading copy.
public sealed class StateBuffer
{
    private readonly GameState[] _states;
    private readonly int[] _frames;

    public StateBuffer(int capacity)
    {
        _states = new GameState[capacity];
        _frames = new int[capacity];
        System.Array.Fill(_frames, -1);
    }

    public int Capacity => _states.Length;

    public void Save(in GameState state)
    {
        var slot = state.Frame % _states.Length;
        _states[slot] = state;
        _frames[slot] = state.Frame;
    }

    // Restores the state saved at this frame. False if it was never saved or has been overwritten.
    public bool TryLoad(int frame, ref GameState state)
    {
        var slot = frame % _states.Length;
        if (frame < 0 || _frames[slot] != frame)
            return false;
        state = _states[slot];
        return true;
    }
}
