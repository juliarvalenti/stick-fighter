using System;
using System.IO;
using StickFighter.Sim.Characters;
using StickFighter.Sim.Collision;
using StickFighter.Sim.Numerics;
using Xunit;

namespace StickFighter.Sim.Tests;

public class CharacterDataTests
{
    private static readonly string FilePath = Path.Combine(RepoRoot(), "data", "characters", "placeholder.json");
    private static string Json => File.ReadAllText(FilePath);
    private static Character Data => Characters.Characters.Placeholder;

    [Fact]
    public void StatsComeFromTheBaseline()
    {
        Assert.Equal(1000, Data.Stats.Health);
        Assert.Equal(1000, Data.Stats.Stun);
        Assert.Equal(Fix.FromRatio(7517, 1000), Data.Stats.WalkForward);
        Assert.True(Data.Stats.WalkBack < Data.Stats.WalkForward);
        Assert.Equal(18, Data.Stats.ForwardDashFrames);
        Assert.Equal(27, Data.Stats.BackDashFrames);
        Assert.Equal(8, Data.Stats.BackDashInvincibleFrames);
        Assert.Equal((4, 36, 4), (Data.Stats.PreJumpFrames, Data.Stats.AirborneFrames, Data.Stats.LandingFrames));
    }

    [Fact]
    public void LoadingTheSameFileTwiceGivesTheSameHash()
    {
        var a = CharacterLoader.Load(Json, "a");
        var b = CharacterLoader.Load(Json, "b");
        Assert.Equal(a.DataHash, b.DataHash);
        Assert.Equal(Data.DataHash, a.DataHash); // the embedded copy is the same file
    }

    [Fact]
    public void WhitespaceDoesNotChangeTheHashButANumberDoes()
    {
        var reformatted = Json.Replace("\n", "\n  ", StringComparison.Ordinal);
        Assert.Equal(Data.DataHash, CharacterLoader.Load(reformatted, "x").DataHash);

        var tweaked = Json.Replace("\"walkForward\": 7517", "\"walkForward\": 7518", StringComparison.Ordinal);
        Assert.NotEqual(Data.DataHash, CharacterLoader.Load(tweaked, "x").DataHash);
    }

    [Theory]
    [InlineData("\"frame\": 5, \"hip\": [0, -112], \"angles\": { \"spine\": 30", "\"frame\": 1, \"hip\": [0, -112], \"angles\": { \"spine\": 30", "move 5LP: pose key 3: frame 1 is not after")]
    [InlineData("\"startup\": 4", "\"startup\": 4.5", "move 5LP: 'startup' must be a whole number")]
    [InlineData("\"limb\": \"frontForearm\"", "\"limb\": \"tail\"", "move 5LP: hitbox limb 'tail' is not a limb")]
    [InlineData("\"strength\": \"light\"", "\"strength\": \"mighty\"", "move 5LP: 'strength' = 'mighty' is not one of")]
    [InlineData("\"walkBack\": 5011", "\"walkBack\": 0", "'walkBack' must be positive")]
    [InlineData("\"frame\": 8, \"hip\"", "\"frame\": 11, \"hip\"", "move 5LP: pose key 4: frame 11 is past the end (length 11)")]
    public void BadDataFailsLoudlyNamingWhere(string find, string replace, string message)
    {
        var broken = Json.Replace(find, replace, StringComparison.Ordinal);
        Assert.NotEqual(Json, broken);
        var error = Assert.Throws<FormatException>(() => CharacterLoader.Load(broken, "placeholder.json"));
        Assert.Contains(message, error.Message, StringComparison.Ordinal);
        Assert.StartsWith("placeholder.json: ", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MovesDeriveStunsFromTheirStrength()
    {
        var jab = Data.FindMove("5LP")!;
        Assert.Equal((14, 11), (jab.Hitstun, jab.Blockstun));
        Assert.Equal(11, jab.TotalFrames);
        Assert.False(jab.IsActive(2));
        Assert.True(jab.IsActive(3));
        Assert.True(jab.IsActive(4));
        Assert.False(jab.IsActive(5));
        Assert.Equal(Limb.FrontForearm, jab.HitboxLimb);
    }

    [Fact]
    public void KeysHoldUntilTheNextKey()
    {
        var walk = Data.Pose(FighterAction.WalkForward);
        Assert.Same(walk.Keys[0], walk.At(7));
        Assert.Same(walk.Keys[1], walk.At(8));
        Assert.Same(walk.Keys[0], walk.At(32)); // loops
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "StickFighter.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("repo root");
    }
}

public class PoseTests
{
    private static Character Data => Characters.Characters.Placeholder;

    private static FighterState At(int x, int facing, FighterAction action, int frame = 0) => new()
    {
        Position = new FixVec2(Fix.FromInt(x), Stage.Floor),
        Facing = facing,
        Action = (int)action,
        ActionFrame = frame,
    };

    public static TheoryData<FighterAction, int> AllKeys()
    {
        var data = new TheoryData<FighterAction, int>();
        foreach (FighterAction action in Enum.GetValues<FighterAction>())
        {
            foreach (var key in Data.Pose(action).Keys)
                data.Add(action, key.Frame);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(AllKeys))]
    public void FeetStayOnTheFloor(FighterAction action, int frame)
    {
        var joints = Body.Pose(At(640, 1, action, frame));
        foreach (var foot in new[] { Joint.BackFoot, Joint.FrontFoot })
            Assert.InRange((joints[foot].Y - Stage.Floor).RoundToInt(), -2, 2);
    }

    [Theory]
    [MemberData(nameof(AllKeys))]
    public void ArmsAndHeadsDoNotClipAtMinimumDistance(FighterAction action, int frame)
    {
        // Pushboxes touching: as close as two fighters can stand. (Crouching knees may cross, as
        // legs do in 2D fighters at point-blank; arms and heads must not.)
        var gap = (Body.PushHalfWidth * 2).RoundToInt();
        var p1 = Body.Pose(At(640, 1, action, frame));
        var p2 = Body.Pose(At(640 + gap, -1, action, frame));
        var radius = Data.Skeleton.LimbRadius;

        foreach (var a in new[] { Limb.FrontUpperArm, Limb.FrontForearm })
        {
            foreach (var b in new[] { Limb.FrontUpperArm, Limb.FrontForearm })
                Assert.False(Overlap.Capsules(Body.Segment(p1, a, radius), Body.Segment(p2, b, radius), out _), $"{action} frame {frame}: P1 {a} touches P2 {b}");
        }

        var head = Data.Skeleton.HeadRadius;
        Assert.False(
            Overlap.Capsules(Capsule.Circle(p1[Joint.Head], head), Capsule.Circle(p2[Joint.Head], head), out _),
            $"{action} frame {frame}: heads overlap");
    }

    [Fact]
    public void HurtboxesFollowThePose()
    {
        var stand = Body.Shapes(At(640, 1, FighterAction.Stand));
        var crouch = Body.Shapes(At(640, 1, FighterAction.Crouch));
        // The head (hurtbox 0) drops when crouching.
        Assert.True(crouch.Hurt[0].Shape.A.Y > stand.Hurt[0].Shape.A.Y + Fix.FromInt(30));
        Assert.Equal(1 + 9, stand.HurtCount); // the head circle, the spine, and eight limb bones
    }

    [Fact]
    public void FacingLeftMirrorsThePose()
    {
        var right = Body.Pose(At(640, 1, FighterAction.Stand));
        var left = Body.Pose(At(640, -1, FighterAction.Stand));
        for (var i = 0; i < Skeleton.JointCount; i++)
        {
            Assert.Equal(right[i].Y, left[i].Y);
            Assert.Equal(right[i].X - Fix.FromInt(640), Fix.FromInt(640) - left[i].X);
        }
    }

    [Fact]
    public void WalkingAnimatesFromFrameToFrame()
    {
        var a = Body.Pose(At(640, 1, FighterAction.WalkForward, 0));
        var b = Body.Pose(At(640, 1, FighterAction.WalkForward, 8));
        Assert.NotEqual(a[Joint.FrontFoot], b[Joint.FrontFoot]);
    }
}

public class ActionTests
{
    private static Character Data => Characters.Characters.Placeholder;

    [Fact]
    public void DownCrouchesInPlace()
    {
        var state = GameState.NewMatch(1);
        var x = state.Fighters[0].Position.X;
        Simulation.Step(ref state, new FrameInput(Buttons.Down | Buttons.Right, default));
        Assert.Equal((int)FighterAction.Crouch, state.Fighters[0].Action);
        Assert.Equal(x, state.Fighters[0].Position.X);
    }

    [Fact]
    public void ForwardAndBackWalkAtTheirOwnSpeeds()
    {
        var state = GameState.NewMatch(1);
        var p1 = state.Fighters[0].Position.X;
        var p2 = state.Fighters[1].Position.X;
        Simulation.Step(ref state, new FrameInput(Buttons.Right, Buttons.Right)); // P1 forward, P2 back

        Assert.Equal((int)FighterAction.WalkForward, state.Fighters[0].Action);
        Assert.Equal((int)FighterAction.WalkBack, state.Fighters[1].Action);
        Assert.Equal(p1 + Data.Stats.WalkForward, state.Fighters[0].Position.X);
        Assert.Equal(p2 + Data.Stats.WalkBack, state.Fighters[1].Position.X);
    }

    [Fact]
    public void ActionFrameCountsUpAndResetsOnChange()
    {
        var state = GameState.NewMatch(1);
        var walk = new FrameInput(Buttons.Right, default);
        for (var i = 0; i < 5; i++)
            Simulation.Step(ref state, walk);
        Assert.Equal(4, state.Fighters[0].ActionFrame);

        Simulation.Step(ref state, default);
        Assert.Equal((int)FighterAction.Stand, state.Fighters[0].Action);
        Assert.Equal(0, state.Fighters[0].ActionFrame);
    }
}
