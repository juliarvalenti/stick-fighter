using System.Collections.Generic;

namespace StickFighter.Sim.Training;

// Up to ten seconds of one player's buttons, for the dummy to play back. Directions are kept
// relative to the way the player faced (forward and back, not right and left), so a recording
// made on the right plays back the same way from the left.
public sealed class InputRecording
{
    public const int MaxFrames = 10 * GameInfo.TicksPerSecond;

    private readonly List<Buttons> _frames = new();

    public int Frames => _frames.Count;

    public bool Full => _frames.Count >= MaxFrames;

    public void Clear() => _frames.Clear();

    // Adds a frame of buttons pressed while facing `facing` (+1 right). Past ten seconds, nothing.
    public void Add(Buttons buttons, int facing)
    {
        if (!Full)
            _frames.Add(facing < 0 ? Mirrored(buttons) : buttons);
    }

    // Frame `index` of the recording, for a player now facing `facing`.
    public Buttons At(int index, int facing) => facing < 0 ? Mirrored(_frames[index]) : _frames[index];

    // Left and right swapped.
    public static Buttons Mirrored(Buttons buttons)
    {
        var left = buttons.Has(Buttons.Left);
        var right = buttons.Has(Buttons.Right);
        buttons &= ~(Buttons.Left | Buttons.Right);
        return buttons | (left ? Buttons.Right : Buttons.None) | (right ? Buttons.Left : Buttons.None);
    }
}
