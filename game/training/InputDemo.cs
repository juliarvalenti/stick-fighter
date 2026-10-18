using StickFighter.Sim;

namespace StickFighter.Training;

// The inputs the training-input-display scenario plays as P1 (P2 does nothing), shared with the
// test that checks the log row by row. P1 starts facing right, so Right is 6.
//
//   frames  0-4   neutral
//   5-7     2           a quarter circle forward...
//   8-9     3
//   10-11   6
//   12      6 + LP      ...punch: 236P
//   13-19   neutral
//   20-22   6           a dash: 6, neutral, 6
//   23-24   neutral
//   25-27   6
//   28-34   neutral
//   35      MP          a plink: MP, then LP a frame later while MP is held
//   36-37   MP + LP
//   38-44   neutral
//   45-74   4           holding back to block
//   75-79   neutral
public static class InputDemo
{
    public const int Frames = 80;

    public static FrameInput Script(int frame) => new(P1(frame), Buttons.None);

    private static Buttons P1(int f) => f switch
    {
        >= 5 and <= 7 => Buttons.Down,
        8 or 9 => Buttons.Down | Buttons.Right,
        10 or 11 => Buttons.Right,
        12 => Buttons.Right | Buttons.LightPunch,
        >= 20 and <= 22 => Buttons.Right,
        >= 25 and <= 27 => Buttons.Right,
        35 => Buttons.MediumPunch,
        36 or 37 => Buttons.MediumPunch | Buttons.LightPunch,
        >= 45 and <= 74 => Buttons.Left,
        _ => Buttons.None,
    };
}
