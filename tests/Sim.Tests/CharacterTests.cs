using System;
using System.IO;
using System.Linq;
using StickFighter.Sim.Characters;
using StickFighter.Sim.Collision;
using StickFighter.Sim.Numerics;
using Xunit;

namespace StickFighter.Sim.Tests;

public class CharacterDataTests
{
    private static readonly string Dir = Path.Combine(RepoRoot(), "data", "characters");
    private static string Json => File.ReadAllText(Path.Combine(Dir, "placeholder.json"));
    private static string Moves => File.ReadAllText(Path.Combine(Dir, "placeholder.moves.json"));
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
        var a = CharacterLoader.Load(Json, Moves, "a");
        var b = CharacterLoader.Load(Json, Moves, "b");
        Assert.Equal(a.DataHash, b.DataHash);
        Assert.Equal(Data.DataHash, a.DataHash); // the embedded copies are the same files
    }

    [Fact]
    public void WhitespaceDoesNotChangeTheHashButANumberDoes()
    {
        var reformatted = Json.Replace("\n", "\n  ", StringComparison.Ordinal);
        Assert.Equal(Data.DataHash, CharacterLoader.Load(reformatted, Moves, "x").DataHash);

        var tweaked = Json.Replace("\"walkForward\": 7517", "\"walkForward\": 7518", StringComparison.Ordinal);
        Assert.NotEqual(Data.DataHash, CharacterLoader.Load(tweaked, Moves, "x").DataHash);

        var tweakedMove = ReplaceFirst(Moves, "\"damage\": 20", "\"damage\": 21");
        Assert.NotEqual(Data.DataHash, CharacterLoader.Load(Json, tweakedMove, "x").DataHash);
    }

    // Breaks the first occurrence in the compiled moves (5LP is the first move).
    [Theory]
    [InlineData("\"frame\": 7, \"hip\"", "\"frame\": 3, \"hip\"", "move 5LP: pose key 6: frame 3 is not after the previous key's frame 6")]
    [InlineData("\"startup\": 4,", "\"startup\": 4.5,", "move 5LP: 'startup' must be a whole number")]
    [InlineData("\"limb\": \"frontForearm\"", "\"limb\": \"tail\"", "move 5LP: 'limb' = 'tail' is not one of")]
    [InlineData("\"strength\": \"light\"", "\"strength\": \"mighty\"", "move 5LP: 'strength' = 'mighty' is not one of")]
    [InlineData("\"frame\": 10, \"hip\"", "\"frame\": 11, \"hip\"", "move 5LP: pose key 9: frame 11 is past the end (length 11)")]
    // Cancels only go forwards: a normal chains into ground normals, and a special cancels into nothing.
    [InlineData("\"chains\": [\"5LP\", \"2LP\"]", "\"chains\": [\"5LP\", \"236P\"]", "move 5LP: chains into '236P', which isn't a ground normal")]
    [InlineData("\"chains\": [\"5LP\", \"2LP\"]", "\"chains\": [\"5LP\", \"j.LP\"]", "move 5LP: chains into 'j.LP', which isn't a ground normal")]
    [InlineData("\"chains\": [\"5LP\", \"2LP\"]", "\"chains\": [\"5LP\", \"5XX\"]", "move 5LP: chains into '5XX', which isn't a move")]
    [InlineData("\"kind\": \"special\",", "\"kind\": \"special\", \"specialCancel\": true,", "move 236P: only ground normals can cancel or chain")]
    public void BadMoveDataFailsLoudlyNamingWhere(string find, string replace, string message)
    {
        var broken = ReplaceFirst(Moves, find, replace);
        var error = Assert.Throws<FormatException>(() => CharacterLoader.Load(Json, broken, "placeholder.json"));
        Assert.Contains(message, error.Message, StringComparison.Ordinal);
        Assert.StartsWith("placeholder.json: ", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BadCharacterDataFailsLoudly()
    {
        var broken = Json.Replace("\"walkBack\": 5011", "\"walkBack\": 0", StringComparison.Ordinal);
        var error = Assert.Throws<FormatException>(() => CharacterLoader.Load(broken, Moves, "placeholder.json"));
        Assert.Contains("'walkBack' must be positive", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AllBaselineNormalsAndTheFireballAreLoaded()
    {
        string[] inputs =
        {
            "5LP", "5MP", "5HP", "5LK", "5MK", "5HK", "2LP", "2MP", "2HP", "2LK", "2MK", "2HK",
            "j.LP", "j.MP", "j.HP", "j.LK", "j.MK", "j.HK", "LP+LK", "236P",
        };
        foreach (var input in inputs)
            Assert.NotNull(Data.FindMove(input));
        Assert.Equal(inputs.Length, Data.Moves.Length);
        Assert.Equal(Guard.Low, Data.FindMove("2HK")!.Guard);
        Assert.Equal(15, Data.FindMove("2MK")!.Blockstun);
        Assert.NotNull(Data.FindMove("236P")!.Spawn);
        Assert.Equal(MoveKind.Throw, Data.FindMove("LP+LK")!.Kind);
    }

    private static string ReplaceFirst(string text, string find, string replace)
    {
        var at = text.IndexOf(find, StringComparison.Ordinal);
        Assert.True(at >= 0, $"'{find}' not found");
        return string.Concat(text.AsSpan(0, at), replace, text.AsSpan(at + find.Length));
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
        Position = new FixVec2(Fix.FromInt(x), Stage.Training.Floor),
        Facing = facing,
        Action = (int)action,
        ActionFrame = frame,
    };

    public static TheoryData<FighterAction, int> AllKeys() => Keys();

    // Lying down, the feet are off the floor.
    public static TheoryData<FighterAction, int> StandingKeys() => Keys(except: FighterAction.Knockdown);

    // In a throw the fighters hold each other, so their arms touch by design.
    public static TheoryData<FighterAction, int> SeparateKeys() => Keys(except: [FighterAction.Thrown, FighterAction.Throwing]);

    // Ground stance poses only: an attack plays its move's track (the move compiler checks those),
    // and the jump pose is in the air, legs tucked above where the feet would be.
    private static TheoryData<FighterAction, int> Keys(params FighterAction[] except)
    {
        var data = new TheoryData<FighterAction, int>();
        foreach (var action in Enum.GetValues<FighterAction>().Where(a => a is not (FighterAction.Attack or FighterAction.Jump) && !except.Contains(a)))
        {
            foreach (var key in Data.Pose(action).Keys)
                data.Add(action, key.Frame);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(AllKeys))]
    public void NothingGoesThroughTheFloor(FighterAction action, int frame)
    {
        var joints = Body.Pose(At(640, 1, action, frame));
        for (var i = 0; i < Skeleton.JointCount; i++)
            Assert.True((joints[i].Y - Stage.Training.Floor).RoundToInt() <= 2, $"{action} frame {frame}: joint {i} below the floor");
    }

    [Theory]
    [MemberData(nameof(StandingKeys))]
    public void FeetStayOnTheFloor(FighterAction action, int frame)
    {
        var joints = Body.Pose(At(640, 1, action, frame));
        foreach (var foot in new[] { Joint.BackFoot, Joint.FrontFoot })
            Assert.InRange((joints[foot].Y - Stage.Training.Floor).RoundToInt(), -2, 2);
    }

    [Theory]
    [MemberData(nameof(SeparateKeys))]
    public void ArmsAndHeadsDoNotClipAtMinimumDistance(FighterAction action, int frame)
    {
        // Pushboxes touching: as close as two fighters can stand. (Crouching knees may cross, as
        // legs do in 2D fighters at point-blank; arms must not.) Heads must not either, except
        // when both are crouched: a crouch at two-thirds height has to lean its head forward, so
        // two crouchers' heads overlap at point-blank, as sprites do in SF4. That's a picture,
        // not a collision (hurtboxes don't push); decided in the Ground movement task.
        var p1 = Body.Pose(At(640, 1, action, frame));
        var p2 = Body.Pose(At(640 + PointBlank, -1, action, frame));
        AssertArmsClear(p1, p2, $"{action} frame {frame}");
        if (action is not (FighterAction.Crouch or FighterAction.BlockCrouch))
            AssertHeadsClear(p1, p2, $"{action} frame {frame}");
    }

    public static TheoryData<FighterAction, FighterAction> StandingAgainstCrouching()
    {
        var data = new TheoryData<FighterAction, FighterAction>();
        foreach (var low in new[] { FighterAction.Crouch, FighterAction.BlockCrouch })
        {
            foreach (var tall in new[] { FighterAction.Stand, FighterAction.BlockStand, FighterAction.WalkForward, FighterAction.CrouchDown })
                data.Add(tall, low);
        }
        return data;
    }

    // Standing against crouching stays clip-free, heads included.
    [Theory]
    [MemberData(nameof(StandingAgainstCrouching))]
    public void StandingAndCrouchingDoNotClip(FighterAction tall, FighterAction low)
    {
        foreach (var lowKey in Data.Pose(low).Keys)
        {
            foreach (var tallKey in Data.Pose(tall).Keys)
            {
                var what = $"{tall} frame {tallKey.Frame} vs {low} frame {lowKey.Frame}";
                var p1 = Body.Pose(At(640, 1, tall, tallKey.Frame));
                var p2 = Body.Pose(At(640 + PointBlank, -1, low, lowKey.Frame));
                AssertArmsClear(p1, p2, what);
                AssertHeadsClear(p1, p2, what);
            }
        }
    }

    // A capsule at standing chest height, where a high attack lands, hits a standing fighter and
    // passes over a crouching one: the crouch actually ducks.
    [Theory]
    [InlineData(FighterAction.Crouch)]
    [InlineData(FighterAction.BlockCrouch)]
    public void TheCrouchDucksUnderAHighAttack(FighterAction low)
    {
        var standing = Body.Pose(At(640, 1, FighterAction.Stand));
        var chestY = (standing[Joint.Neck].Y * 3 + standing[Joint.Hip].Y) / 4; // high on the chest
        var attack = new Capsule(new FixVec2(Fix.FromInt(560), chestY), new FixVec2(Fix.FromInt(720), chestY), Fix.FromInt(8));

        Assert.True(Hits(Body.Shapes(At(640, 1, FighterAction.Stand)), attack));
        foreach (var key in Data.Pose(low).Keys)
            Assert.False(Hits(Body.Shapes(At(640, 1, low, key.Frame)), attack), $"{low} frame {key.Frame} is hit by a high attack");

        // And it's about two-thirds of standing height (60–65%, measured to the top of the head).
        var floor = Stage.Training.Floor;
        var standTop = floor - (standing[Joint.Head].Y - Data.Skeleton.HeadRadius);
        var crouching = Body.Pose(At(640, 1, low));
        var crouchTop = floor - (crouching[Joint.Head].Y - Data.Skeleton.HeadRadius);
        Assert.InRange((crouchTop * 100 / standTop).RoundToInt(), 60, 66);
    }

    private static int PointBlank => (Body.PushHalfWidth * 2).RoundToInt();

    private static bool Hits(in FighterShapes shapes, in Capsule attack)
    {
        for (var i = 0; i < shapes.HurtCount; i++)
        {
            if (Overlap.Capsules(shapes.Hurt[i].Shape, attack, out _))
                return true;
        }
        return false;
    }

    private static void AssertArmsClear(in Joints p1, in Joints p2, string what)
    {
        var radius = Data.Skeleton.LimbRadius;
        foreach (var a in new[] { Limb.FrontUpperArm, Limb.FrontForearm })
        {
            foreach (var b in new[] { Limb.FrontUpperArm, Limb.FrontForearm })
                Assert.False(Overlap.Capsules(Body.Segment(p1, a, radius), Body.Segment(p2, b, radius), out _), $"{what}: P1 {a} touches P2 {b}");
        }
    }

    private static void AssertHeadsClear(in Joints p1, in Joints p2, string what)
    {
        var head = Data.Skeleton.HeadRadius;
        Assert.False(
            Overlap.Capsules(Capsule.Circle(p1[Joint.Head], head), Capsule.Circle(p2[Joint.Head], head), out _),
            $"{what}: heads overlap");
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
