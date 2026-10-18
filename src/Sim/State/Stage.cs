using StickFighter.Sim.Numerics;

namespace StickFighter.Sim;

// Where a match is fought: the walls, the floor, and how far apart the fighters may get. A match
// is created with one and keeps it in GameState, so it's checksummed and travels with rollback;
// a replay records it, so a replay always plays on the stage it was recorded on.
//
// All in whole pixels, so a replay can write the stage exactly as plain integers.
public readonly struct Stage
{
    public const int EncodedSize = 4 * 8;

    // One screen: the furthest apart the fighters can get is what the camera can show.
    public const int ScreenWidth = 1280;

    // The training stage: two screens wide, so the camera has room to follow.
    public static readonly Stage Training = new(0, 2 * ScreenWidth, 600, ScreenWidth);

    public readonly Fix LeftWall;
    public readonly Fix RightWall;
    public readonly Fix Floor;

    // From the outer edge of one fighter's pushbox to the outer edge of the other's, so both
    // bodies fit in this width at once.
    public readonly Fix MaxSeparation;

    public Stage(int leftWall, int rightWall, int floor, int maxSeparation)
    {
        LeftWall = Fix.FromInt(leftWall);
        RightWall = Fix.FromInt(rightWall);
        Floor = Fix.FromInt(floor);
        MaxSeparation = Fix.FromInt(maxSeparation);
    }

    public Fix Center => (LeftWall + RightWall) / 2;

    public void Encode(ref StateWriter writer)
    {
        writer.Write(LeftWall);
        writer.Write(RightWall);
        writer.Write(Floor);
        writer.Write(MaxSeparation);
    }
}
