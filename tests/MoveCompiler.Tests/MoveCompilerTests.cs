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
    public void AKickThatSwingsOutInRecoveryFailsTheChecks()
    {
        var (output, _) = Build.Run(Character, Library, Scripts, "placeholder");
        var character = Sim.Characters.CharacterLoader.Load(Character, output, "placeholder");
        var move = character.FindMove("5MK")!;
        var keys = Enumerable.Range(0, move.TotalFrames).Select(move.Track.At).ToList();

        // Swing the kicking leg further out on one recovery frame, as a joint-angle blend would.
        var frame = move.FirstActiveFrame + move.Active + 2;
        keys[frame] = keys[frame] with { FrontHip = keys[frame].FrontHip - Sim.Numerics.Angle.FromDegrees(40) };

        using var library = System.Text.Json.JsonDocument.Parse(Library);
        using var scripts = System.Text.Json.JsonDocument.Parse(Scripts);
        var script = scripts.RootElement.GetProperty("moves").EnumerateArray().First(m => m.GetProperty("input").GetString() == "5MK");
        var compiled = new Compiler(Sim.Characters.CharacterLoader.Load(Character, null, "placeholder"), library.RootElement).Compile(script.Clone());

        var problems = Checks.Run(compiled, keys, character);
        Assert.Contains(problems, p => p.Contains($"move 5MK frame {frame}: in recovery the front foot swings", StringComparison.Ordinal));
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
