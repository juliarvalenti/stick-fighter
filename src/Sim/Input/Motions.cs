namespace StickFighter.Sim;

// Special-move motions, read from the input history relative to the fighter's facing. Lenient, as in
// SF4: the directions only have to come in order within the window, with anything in between
// (2, 1, 2, 3, 6 is still a quarter circle), and the button can come a few frames after the last one.
public static class Motions
{
    // A quarter circle forward (2, 3, 6) finished by a button pressed on frame `at` (or, for negative
    // edge, released on it): the 6 no earlier than `forwardStart`, and the whole motion no earlier
    // than `start`. (The caller sets those from the window and leniency, skipping hitstop: a motion
    // made during a normal that connected stretches across the freeze.)
    public static bool QuarterCircleForward(in InputHistory history, int at, int now, int facing, int start, int forwardStart)
    {
        var forward = Latest(history, now, facing, 6, at, forwardStart);
        if (forward < 0)
            return false;
        var downForward = Latest(history, now, facing, 3, forward - 1, start);
        if (downForward < 0)
            return false;
        return Latest(history, now, facing, 2, downForward - 1, start) >= 0;
    }

    // The latest frame from `from` back to `to` (inclusive) whose direction is `direction`, or -1.
    // Taking the latest of each step in turn finds the motion if any order of frames would.
    private static int Latest(in InputHistory history, int now, int facing, int direction, int from, int to)
    {
        for (var frame = from; frame >= to && frame >= 0; frame--)
        {
            if (Numpad.Direction(history.At(frame, now), facing) == direction)
                return frame;
        }
        return -1;
    }
}
