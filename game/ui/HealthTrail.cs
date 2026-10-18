using StickFighter.Sim;

namespace StickFighter.UI;

// The damage trail behind each health bar: health just lost stays drawn for a moment, then drains
// down to what's left. Worked out from the health on recent frames, stored by frame number, so a
// frame presented again (after a rollback) replaces its earlier record instead of adding to it.
public sealed class HealthTrail
{
    // Frames the lost health holds before it starts to drain, and how fast it drains.
    public const int HoldFrames = 30;
    public const int DrainPerFrame = 12;

    // Enough history for the largest drop to drain away (1000 health at 12 a frame, after the hold).
    private const int Window = 128;

    private readonly (int Frame, int Health)[,] _seen = new (int, int)[GameState.PlayerCount, Window];

    public HealthTrail()
    {
        for (var p = 0; p < GameState.PlayerCount; p++)
        {
            for (var i = 0; i < Window; i++)
                _seen[p, i] = (-1, 0);
        }
    }

    public void Record(in GameState state)
    {
        for (var p = 0; p < GameState.PlayerCount; p++)
            _seen[p, state.Frame % Window] = (state.Frame, state.Fighters[p].Health);
    }

    // The trail's end on `frame`: the highest recent health, less what has drained since.
    public int At(int player, int frame, int health)
    {
        var trail = health;
        for (var age = 0; age < Window && age <= frame; age++)
        {
            var (seenFrame, seenHealth) = _seen[player, (frame - age) % Window];
            if (seenFrame != frame - age)
                continue;
            var drained = System.Math.Max(0, age - HoldFrames) * DrainPerFrame;
            trail = System.Math.Max(trail, seenHealth - drained);
        }
        return trail;
    }
}
