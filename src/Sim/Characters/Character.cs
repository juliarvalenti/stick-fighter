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

    // Going from standing to crouching: still tall, but counting as crouching (it blocks low and
    // starts crouching normals). Becomes Crouch after the character's crouch transition frames.
    CrouchDown = 7,

    // Held in the opponent's throw, until it's teched or completes.
    Thrown = 8,

    // Holding a throw that connected, through the tech check and the follow-through.
    Throwing = 9,

    // Lying down after a throw (later, after sweeps too). Nothing hits a fighter lying down.
    Knockdown = 10,

    // A jump: crouching to spring on the ground (PreJump, the character's pre-jump frames), in the
    // air on a fixed arc (Jump), and landing (Landing, the character's landing frames).
    PreJump = 11,
    Jump = 12,
    Landing = 13,

    // Dashing forward or back (66 or 44): a fixed distance on a curve that starts fast and eases to
    // a stop, then recovery. The back-dash's first frames are invincible.
    DashForward = 14,
    DashBack = 15,

    // Dizzy: the stun gauge filled. Can't act or guard for the character's dizzy frames; a hit
    // ends it.
    Dizzy = 16,

    // Performing a move; the pose comes from the move's track, not a stance pose. Last, because
    // every action before it has a stance pose at the same index.
    Attack = 17,
}

public enum Strength
{
    Light,
    Medium,
    Heavy,
}

public enum MoveKind
{
    // Strikes with its hitbox limb.
    Normal,

    // Grabs an opponent within throw range on its active frames; has no hitbox.
    Throw,
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
    Fix ProximityGuard,
    // Frames of CrouchDown before the crouch pose.
    int CrouchTransitionFrames,
    // Walking into a fighter who isn't walking in, the share of the walker's step that moves them
    // along (the walker keeps the same share, so the two move together at this fraction of its speed).
    Fix PushShare,
    // Throws (see the comment in the character data): range feet to feet, tech timing, landing
    // distance, how long the thrower holds on, and the knockdown and wakeup that follow.
    Fix ThrowRange,
    int TechFrame,
    int TechWindow,
    Fix TechSeparation,
    Fix ThrowDistance,
    int ThrowFollowThrough,
    int KnockdownFrames,
    int WakeupThrowInvulnerable,
    // The jump arc, per frame: upward speed at takeoff, gravity, and the sideways drift of a forward
    // or back jump. The loader checks the arc lands in exactly AirborneFrames.
    Fix JumpVelocity,
    Fix Gravity,
    Fix JumpForward,
    Fix JumpBack,
    // Dashes: how far each goes, and how many of its frames (ForwardDashFrames, BackDashFrames) at
    // the end are recovery, standing still. The second tap of 66 or 44 must come within
    // DashInputWindow frames of the first.
    Fix ForwardDashDistance,
    Fix BackDashDistance,
    int ForwardDashRecovery,
    int BackDashRecovery,
    int DashInputWindow,
    // Hit states (see the comment in the character data): extra hitstun on a crouching defender,
    // SF4's damage reduction at low health (on or off), and the fall an air hit knocks a fighter
    // into: upward speed, and drift away from the attacker, per frame.
    int CrouchHitstunBonus,
    bool LowLifeReduction,
    Fix AirHitRise,
    Fix AirHitDrift,
    // Knockdowns and stun (see the comment in the character data): a quick rise (down or two buttons
    // within QuickRiseWindow frames before landing from a juggle) gets up after QuickRiseFrames
    // instead of KnockdownFrames; a full stun gauge (Stun) means DizzyFrames dizzy; the gauge
    // recovers StunRecoveryRate a frame once StunRecoveryDelay frames have passed since the last hit.
    int QuickRiseFrames,
    int QuickRiseWindow,
    int DizzyFrames,
    int StunRecoveryDelay,
    int StunRecoveryRate,
    // Specials: a motion completes within MotionWindow frames, and its button may come up to
    // MotionButtonLeniency frames after the last direction. A fireball flies at its strength's speed.
    int MotionWindow,
    int MotionButtonLeniency,
    Fix ProjectileSpeedLight,
    Fix ProjectileSpeedMedium,
    Fix ProjectileSpeedHeavy);

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
    MoveKind Kind,
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
    // Juggles, SF4's system: an air hit sets the falling fighter's juggle count to JuggleStart; a
    // later hit connects only if its JuggleLimit is at least the count, and adds JuggleIncrease.
    // Normals default to 1, 1, 0: they knock a fighter into a fall but can't hit one already falling.
    int JuggleStart,
    int JuggleIncrease,
    int JuggleLimit,
    // A hit on a grounded fighter knocks it down instead of into hitstun, with no quick rise
    // (sweeps). Air hits are soft knockdowns anyway, when the fall lands.
    bool HardKnockdown,
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

    // Done from a crouch (numpad 1, 2 or 3), so it keeps the crouching pushbox and ends crouched.
    public bool Crouching => Input.Length > 0 && Input[0] is '1' or '2' or '3';

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

    // Frames both fighters freeze when this move connects. There are no reliable SF4 numbers, so
    // these are placeholders by strength, on the playtest queue: 8/10/12 on hit, 2 less on block.
    public int HitstopOnHit => Strength switch
    {
        Strength.Light => 8,
        Strength.Medium => 10,
        _ => 12,
    };

    public int HitstopOnBlock => HitstopOnHit - 2;
}

public sealed record Character(
    string Name,
    Stats Stats,
    SkeletonSize Skeleton,
    ImmutableArray<PoseTrack> Poses,
    // The pushbox's height in each stance, by FighterAction like Poses.
    ImmutableArray<Fix> PushHeights,
    ImmutableArray<Move> Moves,
    ulong DataHash)
{
    public PoseTrack Pose(FighterAction action) => Poses[(int)action];

    public Fix PushHeight(FighterAction action) => PushHeights[(int)action];

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
