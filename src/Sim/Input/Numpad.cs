namespace StickFighter.Sim;

// Numpad notation, relative to facing: 6 is always forward, 4 back, 8 up, 2 down, 5 neutral.
//   7 8 9
//   4 5 6
//   1 2 3
public static class Numpad
{
    public const int Neutral = 5;

    // facing is +1 for facing right, -1 for facing left. Expects SOCD-cleaned buttons.
    public static int Direction(Buttons buttons, int facing)
    {
        var horizontal = (buttons.Has(Buttons.Right) ? 1 : 0) - (buttons.Has(Buttons.Left) ? 1 : 0);
        var vertical = (buttons.Has(Buttons.Up) ? 1 : 0) - (buttons.Has(Buttons.Down) ? 1 : 0);
        return Neutral + horizontal * facing + 3 * vertical;
    }

    // -1 back, 0 neutral, +1 forward.
    public static int Horizontal(int direction) => (direction - 1) % 3 - 1;

    // -1 down, 0 neutral, +1 up.
    public static int Vertical(int direction) => (direction - 1) / 3 - 1;
}
