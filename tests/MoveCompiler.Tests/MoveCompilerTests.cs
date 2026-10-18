using System;
using System.IO;
using System.Linq;
using StickFighter.MoveCompiler;
using Xunit;

namespace StickFighter.MoveCompiler.Tests;

public class MoveCompilerTests
{
    private static readonly string Root = FindRoot();
    private static string Character => Read("data", "characters", "placeholder.json");
    private static string Library => Read("data", "moves", "library.json");
    private static string Scripts => Read("data", "moves", "placeholder.scripts.json");

    [Fact]
    public void TheCommittedMovesMatchTheScripts()
    {
        var (output, problems) = Build.Run(Character, Library, Scripts, "placeholder");
        Assert.Empty(problems);
        Assert.Equal(Read("data", "characters", "placeholder.moves.json"), output);
    }

    [Fact]
    public void CompilingIsDeterministic()
    {
        Assert.Equal(Build.Run(Character, Library, Scripts, "placeholder").Output, Build.Run(Character, Library, Scripts, "placeholder").Output);
    }

    [Fact]
    public void ABackwardsElbowFailsTheChecks()
    {
        // A library pose with the front elbow bent the wrong way (+40°), used by a move.
        var library = Library.Replace("\"poses\": {", "\"poses\": {\n    \"broken\": { \"hip\": [0, -112], \"angles\": { \"spine\": 0, \"neck\": 0, \"backShoulder\": 291, \"backElbow\": -519, \"frontShoulder\": -27, \"frontElbow\": 455, \"backHip\": -18, \"backKnee\": 317, \"frontHip\": -291, \"frontKnee\": 335 } },", StringComparison.Ordinal);
        var scripts = Scripts.Replace("\"do\": [\"startup: reach:front-hand opponent-chest 0.5\", \"lean:4\",", "\"do\": [\"startup: pose:broken\",", StringComparison.Ordinal);
        Assert.NotEqual(Scripts, scripts);

        var (_, problems) = Build.Run(Character, library, scripts, "placeholder");
        Assert.Contains(problems, p => p.Contains("move 5LP", StringComparison.Ordinal) && p.Contains("front elbow bends backwards", StringComparison.Ordinal));
    }

    [Fact]
    public void AFootThroughTheFloorFailsTheChecks()
    {
        var library = Library.Replace("\"poses\": {", "\"poses\": {\n    \"sunk\": { \"hip\": [0, -90], \"angles\": { \"spine\": 0, \"neck\": 0, \"backShoulder\": 291, \"backElbow\": -519, \"frontShoulder\": -27, \"frontElbow\": -1830, \"backHip\": -18, \"backKnee\": 317, \"frontHip\": -291, \"frontKnee\": 335 } },", StringComparison.Ordinal);
        var scripts = ReplaceFirst(Scripts, "\"startup: pose:windup-punch\",", "\"startup: pose:sunk\","); // 5MP's

        var (_, problems) = Build.Run(Character, library, scripts, "placeholder");
        Assert.Contains(problems, p => p.Contains("move 5MP", StringComparison.Ordinal) && p.Contains("below the floor", StringComparison.Ordinal));
    }

    [Fact]
    public void AKickThatSweepsOutPastItsReachInRecoveryFailsTheChecks()
    {
        var (character, compiled, keys) = Compiled("5MK");

        // Straighten the kicking leg out level in front on one recovery frame: a second sweep.
        var frame = compiled.Startup - 1 + compiled.Active + 2;
        keys[frame] = keys[frame] with { FrontHip = Sim.Numerics.Angle.FromDegrees(-90), FrontKnee = Sim.Numerics.Angle.FromDegrees(0) };

        var problems = Checks.Run(compiled, keys, character);
        Assert.Contains(problems, p => p.Contains($"move 5MK frame {frame}: in recovery the front foot is", StringComparison.Ordinal)
            && p.Contains("past its active reach", StringComparison.Ordinal));
    }

    [Fact]
    public void TheCompiledMovesFoldBackInsteadOfSweeping()
    {
        // The arc home can't pass its reach, so every move's recovery stays inside it.
        var (_, problems) = Build.Run(Character, Library, Scripts, "placeholder");
        Assert.DoesNotContain(problems, p => p.Contains("past its active reach", StringComparison.Ordinal));
    }

    [Fact]
    public void AnArmOnlyMoveFailsTheWholeBodyCheck()
    {
        var (character, compiled, keys) = Compiled("5MP");

        // Freeze everything but the arms at the stance: one arm punches, the body stands still.
        var stance = character.Pose(Sim.Characters.FighterAction.Stand).Keys[0];
        for (var i = 0; i < keys.Count; i++)
        {
            keys[i] = keys[i] with
            {
                Hip = stance.Hip,
                Spine = stance.Spine,
                Neck = stance.Neck,
                BackHip = stance.BackHip,
                BackKnee = stance.BackKnee,
                FrontHip = stance.FrontHip,
                FrontKnee = stance.FrontKnee,
            };
        }

        var problems = Checks.Run(compiled, keys, character);
        Assert.Contains(problems, p => p.StartsWith("move 5MP: the hip moves 0 px", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.StartsWith("move 5MP: the torso turns 0°", StringComparison.Ordinal));
    }

    [Fact]
    public void AJointThatSnapsOutsideTheStrikeFailsTheChecks()
    {
        var (character, compiled, keys) = Compiled("5HP");

        // Fling the back elbow 90° for one recovery frame.
        var frame = compiled.Startup - 1 + compiled.Active + 4;
        keys[frame] = keys[frame] with { BackElbow = keys[frame].BackElbow - Sim.Numerics.Angle.FromDegrees(90) };

        var problems = Checks.Run(compiled, keys, character);
        Assert.Contains(problems, p => p.Contains($"move 5HP frame {frame}: the back elbow turns", StringComparison.Ordinal));
    }

    [Fact]
    public void EveryStrikeMovesTheWholeBody()
    {
        // Each ground normal's hip shifts and torso turns between the stance and its first active frame.
        var character = Sim.Characters.Characters.Placeholder;
        foreach (var move in character.Moves.Where(m => !m.Input.StartsWith("j.", StringComparison.Ordinal)))
        {
            var stance = character.Pose(Compiler.DefaultStance(move.Input) == "crouch-guard"
                ? Sim.Characters.FighterAction.Crouch : Sim.Characters.FighterAction.Stand).Keys[0];
            var hit = move.Track.At(move.FirstActiveFrame);
            Assert.True(Sim.Numerics.Fix.Abs(hit.Hip.X - stance.Hip.X) >= Sim.Numerics.Fix.FromInt(4), $"{move.Input}: the hip doesn't shift");
            Assert.NotEqual(stance.Spine, hit.Spine);
        }
    }

    [Fact]
    public void TheStanceBreathesWithTheFeetPlanted()
    {
        // The committed character file is the generator's output, and running it again changes nothing.
        Assert.Equal(Character, Breathing.Apply(Character));

        var character = Sim.Characters.CharacterLoader.Load(Character, null, "placeholder");
        foreach (var action in new[] { Sim.Characters.FighterAction.Stand, Sim.Characters.FighterAction.Crouch })
        {
            var track = character.Pose(action);
            Assert.Equal(Breathing.Length, track.Length);
            var rest = track.Keys[0];
            var restFeet = Feet(character, rest);
            var depth = 0;
            for (var frame = 0; frame < track.Length; frame++)
            {
                var key = track.At(frame);
                depth = Math.Max(depth, (key.Hip.Y - rest.Hip.Y).RoundToInt());
                Assert.Equal(restFeet, Feet(character, key));
            }
            Assert.InRange(depth, 1, 2);
        }
    }

    private static (int, int, int, int) Feet(Sim.Characters.Character character, Sim.Characters.PoseKey key)
    {
        var joints = Sim.Characters.Skeleton.Solve(character.Skeleton, key, Sim.Numerics.FixVec2.Zero, 1);
        var back = joints[Sim.Characters.Joint.BackFoot];
        var front = joints[Sim.Characters.Joint.FrontFoot];
        return (back.X.RoundToInt(), back.Y.RoundToInt(), front.X.RoundToInt(), front.Y.RoundToInt());
    }

    // A move compiled from the committed scripts, with its integer keys one per frame to tamper with.
    private static (Sim.Characters.Character, CompiledMove, System.Collections.Generic.List<Sim.Characters.PoseKey>) Compiled(string input)
    {
        var (output, _) = Build.Run(Character, Library, Scripts, "placeholder");
        var character = Sim.Characters.CharacterLoader.Load(Character, output, "placeholder");
        var move = character.FindMove(input)!;
        var keys = Enumerable.Range(0, move.TotalFrames).Select(move.Track.At).ToList();

        using var library = System.Text.Json.JsonDocument.Parse(Library);
        using var scripts = System.Text.Json.JsonDocument.Parse(Scripts);
        var script = scripts.RootElement.GetProperty("moves").EnumerateArray().First(m => m.GetProperty("input").GetString() == input);
        var compiled = new Compiler(Sim.Characters.CharacterLoader.Load(Character, null, "placeholder"), library.RootElement).Compile(script.Clone());
        return (character, compiled, keys);
    }

    [Theory]
    [InlineData("\"lean:4\",", "\"wiggle:4\",", "move 5LP: unknown verb 'wiggle'")]
    [InlineData("reach:front-hand opponent-chest 0.5\"", "reach:front-hand opponent-moon 0.5\"", "move 5LP: no landmark 'opponent-moon'")]
    [InlineData("reach:front-hand opponent-chest 0.5\"", "reach:front-hand opponent-chest\"", "move 5LP: reach:front-hand opponent-chest is out of reach")]
    [InlineData("reach:front-hand opponent-chest 0.5\"", "reach:front-hand opponent-chest 1.5\"", "move 5LP: reach amount 1.5 must be between 0 and 1")]
    [InlineData("\"hitbox:front-hand 8\"", "\"lean:0\"", "move 5LP: no hitbox")]
    public void ScriptMistakesNameTheMove(string find, string replace, string message)
    {
        var scripts = ReplaceFirst(Scripts, find, replace);
        var error = Assert.Throws<CompileException>(() => Build.Run(Character, Library, scripts, "placeholder"));
        Assert.Contains(message, error.Message, StringComparison.Ordinal);
    }

    private static string ReplaceFirst(string text, string find, string replace)
    {
        var at = text.IndexOf(find, StringComparison.Ordinal);
        Assert.True(at >= 0, $"'{find}' not found");
        return string.Concat(text.AsSpan(0, at), replace, text.AsSpan(at + find.Length));
    }

    [Fact]
    public void FrameDataIsExactAndTheHitboxIsOutOnlyOnActiveFrames()
    {
        var character = Sim.Characters.Characters.Placeholder;
        foreach (var move in character.Moves)
        {
            Assert.Equal(move.Startup - 1 + move.Active + move.Recovery, move.Track.Length);
            var active = Enumerable.Range(0, move.TotalFrames).Count(move.IsActive);
            Assert.Equal(move.Active, active);
        }
    }

    [Theory]
    [InlineData(0, 4, 0.0, Ease.InOut, 0.15625)]
    [InlineData(3, 4, 0.0, Ease.InOut, 1.0)]
    [InlineData(0, 4, 0.5, Ease.InOut, 0.0)]
    [InlineData(2, 4, 0.0, Ease.Snap, 1.0)]
    public void PhasesReachTheirTargetOnTheirLastFrame(int frame, int length, double hold, Ease ease, double expected)
    {
        Assert.Equal(expected, Compiler.Progress(frame, length, hold, ease), 6);
    }

    [Fact]
    public void LeanIsRelativeToTheStanceSoAStanceChangeKeepsIt()
    {
        // Lean the crouch stance 100 units (about 9°) further and recompile from the same scripts.
        const string rest = "\"frame\": 0, \"hip\": [-60, -30], \"angles\": { \"spine\": 360,";
        Assert.Contains(rest, Character, StringComparison.Ordinal);
        var deeper = ReplaceFirst(Character, rest, rest.Replace("360,", "460,", StringComparison.Ordinal));

        var before = Sim.Characters.CharacterLoader.Load(Character, Build.Run(Character, Library, Scripts, "placeholder").Output, "before");
        var after = Sim.Characters.CharacterLoader.Load(deeper, Build.Run(deeper, Library, Scripts, "placeholder").Output, "after");
        var stanceBefore = before.Pose(Sim.Characters.FighterAction.Crouch).Keys[0].Spine.Units;
        var stanceAfter = after.Pose(Sim.Characters.FighterAction.Crouch).Keys[0].Spine.Units;
        Assert.Equal(100, stanceAfter - stanceBefore);

        // Every crouching normal leans the same amount relative to its stance on every active frame.
        var crouching = before.Moves.Where(m => Compiler.DefaultStance(m.Input) == "crouch-guard").ToList();
        Assert.NotEmpty(crouching);
        foreach (var move in crouching)
        {
            var moved = after.FindMove(move.Input)!;
            for (var frame = move.FirstActiveFrame; frame < move.FirstActiveFrame + move.Active; frame++)
            {
                var leanBefore = move.Track.At(frame).Spine.Units - stanceBefore;
                var leanAfter = moved.Track.At(frame).Spine.Units - stanceAfter;
                Assert.True(Math.Abs(leanAfter - leanBefore) <= 1, $"{move.Input} frame {frame}: lean {leanBefore} became {leanAfter}");
            }
        }
    }

    [Theory]
    [InlineData("5LP", "guard")]
    [InlineData("2MK", "crouch-guard")]
    [InlineData("j.HP", "jump-tuck")]
    [InlineData("236P", "guard")]
    public void StanceFollowsTheInput(string input, string stance)
    {
        Assert.Equal(stance, Compiler.DefaultStance(input));
    }

    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine(new[] { Root }.Concat(parts).ToArray()));

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "StickFighter.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("repo root");
    }
}
