namespace StickFighter;

// A scenario's screen-space UI that changes over the run (the HUD demo) is told the simulation
// frame after each step, so its picture on a captured frame is as repeatable as the fighters'.
// It gets the frame number only, never the game state.
public interface IScenarioClock
{
    void At(int frame);
}
