using System.Collections.Immutable;
using StickFighter.Sim.Numerics;

namespace StickFighter.Sim.Characters;

// What a fighter is doing. The pose is a pure function of (character, action, action frame),
// so it never needs to be saved in GameState.
// (Not "Action": that would clash with System.Action wherever System is imported.)
public enum FighterAction
{
    Stand = 0,
    Crouch = 1,
    WalkForward = 2,
    WalkBack = 3,

    // Reeling from a hit; can't act until hitstun runs out.
    Hitstun = 4,

    // Guarding, standing or crouching: in blockstun, or holding back near an attack (proximity guard).
    BlockStand = 5,
    BlockCrouch = 6,

    // Performing a move; the pose comes from the move's track, not a stance pose. Last, because
    // every action before it has a stance pose at the same index.
    Attack = 7,
}

public enum Strength
{
    Light,
    Medium,
    Heavy,
}

public enum Guard
{
    Mid,
    High,
    Low,
}

// The skeleton's bones, by name in the data. A move's hitbox is one of these segments.
public enum Limb
{
    Spine,
    Neck,
    BackUpperArm,
    BackForearm,
    FrontUpperArm,
    FrontForearm,
    BackThigh,
    BackShin,
    FrontThigh,
    FrontShin,
}

public sealed record Stats(
    int Health,
    int Stun,
    Fix WalkForward,
    Fix WalkBack,
    int ForwardDashFrames,
    int BackDashFrames,
    int BackDashInvincibleFrames,
    int PreJumpFrames,
    int AirborneFrames,
    int LandingFrames,
    Fix PushHalfWidth,
    Fix PushHeight,
    // Holding back with the opponent's attack in startup or active frames and the fighters at most
    // this far apart (feet to feet) puts you in block stance instead of walking back.
    Fix ProximityGuard);

public sealed record SkeletonSize(
    Fix Spine,
    Fix Neck,
    Fix UpperArm,
    Fix Forearm,
    Fix Thigh,
    Fix Shin,
    Fix HeadRadius,
    Fix TorsoRadius,
    Fix LimbRadius);

// One keyed pose. Angles are local: spine from straight up, neck from the spine, shoulders and hips
// from straight down the spine, elbows and knees from their parent bone.
public sealed record PoseKey(
    int Frame,
    FixVec2 Hip,
    Angle Spine,
    Angle Neck,
    Angle BackShoulder,
    Angle BackElbow,
    Angle FrontShoulder,
    Angle FrontElbow,
    Angle BackHip,
    Angle BackKnee,
    Angle FrontHip,
    Angle FrontKnee);

// Keys that hold until the next one (no tweening, as in sprite-based fighters).
// Looping tracks (walks) wrap at Length; a move's track is as long as the move.
public sealed record PoseTrack(int Length, ImmutableArray<PoseKey> Keys)
{
    public PoseKey At(int frame)
    {
        var f = frame % Length;
        if (f < 0)
            f += Length;
        var key = Keys[0];
        for (var i = 1; i < Keys.Length && Keys[i].Frame <= f; i++)
            key = Keys[i];
        return key;
    }
}

public enum HurtMode
{
    // The limb's hurtbox shrinks (a low-profile or tucked limb).
    Shrink,

    // Projectiles pass through the limb's hurtbox.
    IgnoreProjectiles,
}

// A change to one limb's hurtbox on frames From..To of a move. Applied when moves run.
public sealed record HurtModifier(Limb Limb, HurtMode Mode, int From, int To);

// A projectile leaves the given joint on this frame of the move.
public sealed record Spawn(int Frame, Joint From);

public sealed record Move(
    string Name,
    string Input,
    int Startup,
    int Active,
    int Recovery,
    int Damage,
    int Stun,
    Strength Strength,
    Guard Guard,
    int PushbackHit,
    int PushbackBlock,
    int? HitstunOverride,
    int? BlockstunOverride,
    // Damage dealt even when blocked. Normals have none, as in SF4. Only moves with ChipKo
    // (specials and supers) can take the last point of health with it.
    int Chip,
    bool ChipKo,
    Limb HitboxLimb,
    Fix HitboxRadius,
    Fix ActiveBonus,
    ImmutableArray<HurtModifier> Hurt,
    Spawn? Spawn,
    PoseTrack Track)
{
    // Startup includes the first active frame, so frames are counted from 0.
    public int FirstActiveFrame => Startup - 1;
    public int TotalFrames => Startup - 1 + Active + Recovery;

    public bool IsActive(int frame) => frame >= FirstActiveFrame && frame < FirstActiveFrame + Active;

    // SF4's tiers (reference/frame-data-baseline): light 14/11, medium 17/14, heavy 22/18.
    public int Hitstun => HitstunOverride ?? Strength switch
    {
        Strength.Light => 14,
        Strength.Medium => 17,
        _ => 22,
    };

    public int Blockstun => BlockstunOverride ?? Strength switch
    {
        Strength.Light => 11,
        Strength.Medium => 14,
        _ => 18,
    };
}

public sealed record Character(
    string Name,
    Stats Stats,
    SkeletonSize Skeleton,
    ImmutableArray<PoseTrack> Poses,
    ImmutableArray<Move> Moves,
    ulong DataHash)
{
    public PoseTrack Pose(FighterAction action) => Poses[(int)action];

    public Move? FindMove(string input)
    {
        foreach (var move in Moves)
        {
            if (move.Input == input)
                return move;
        }
        return null;
    }
}
