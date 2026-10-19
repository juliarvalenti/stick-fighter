using System.IO;
using System.Linq;
using StickFighter.Editor;
using StickFighter.MoveCompiler;
using Xunit;

namespace StickFighter.Presentation.Tests;

// The pose editor's model on the real character: it opens, edits and saves the move scripts, and
// recompiles them through MoveCompiler in-process on every edit, as `MoveCompiler --check` does.
public class MoveDocumentTests
{
    private static string Root()
    {
        var dir = new DirectoryInfo(System.AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "StickFighter.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("repo root");
    }

    private static string Read(string path) => File.ReadAllText(Path.Combine(Root(), path));

    private static MoveDocument Open() => new(
        Read("data/characters/placeholder.json"), Read("data/moves/library.json"), Read("data/moves/placeholder.scripts.json"));

    // What `MoveCompiler --check` does with a set of saved files: compile the scripts and compare.
    private static void AssertChecks(MoveDocument doc)
    {
        var files = doc.SaveFiles()!;
        var (output, problems) = Build.Run(
            files["data/characters/placeholder.json"], Read("data/moves/library.json"), files["data/moves/placeholder.scripts.json"], "placeholder");
        Assert.Empty(problems);
        Assert.Equal(output, files["data/characters/placeholder.moves.json"]);
        Assert.Equal(files["data/characters/placeholder.json"], Breathing.Apply(files["data/characters/placeholder.json"]));
    }

    [Fact]
    public void SavingUnchangedGivesBackTheSameFiles()
    {
        var doc = Open();
        Assert.True(doc.CanSave);
        Assert.False(doc.Changed);
        var files = doc.SaveFiles()!;
        Assert.Equal(Read("data/moves/placeholder.scripts.json"), files["data/moves/placeholder.scripts.json"]);
        Assert.Equal(Read("data/characters/placeholder.moves.json"), files["data/characters/placeholder.moves.json"]);
        Assert.Equal(Read("data/characters/placeholder.json"), files["data/characters/placeholder.json"]);
    }

    [Fact]
    public void AFrameDataEditRecompilesTheMove()
    {
        var doc = Open();
        doc.Select("5HK");
        var before = doc.Compiled!;
        var (hit, block) = MoveDocument.Advantage(before);

        doc.SetNumber("recovery", before.Recovery + 3);
        doc.SetNumber("damage", 150);
        doc.Cycle("strength", -1); // heavy -> medium

        var after = doc.Compiled!;
        Assert.True(doc.Changed);
        Assert.True(doc.CanSave, string.Join("; ", doc.ProblemsHere));
        Assert.Equal(before.Recovery + 3, after.Recovery);
        Assert.Equal(before.TotalFrames + 3, after.TotalFrames);
        Assert.Equal(before.TotalFrames + 3, after.Track.Length);
        Assert.Equal(150, after.Damage);
        Assert.Equal(Sim.Characters.Strength.Medium, after.Strength);

        // Medium hitstun and blockstun are 5 and 4 frames shorter than heavy, and recovery is 3 longer.
        var (newHit, newBlock) = MoveDocument.Advantage(after);
        Assert.Equal(hit - 5 - 3, newHit);
        Assert.Equal(block - 4 - 3, newBlock);
        AssertChecks(doc);

        // The script changed on its own line, and nowhere else.
        var changed = doc.ScriptsText().Split('\n').Except(Read("data/moves/placeholder.scripts.json").Split('\n')).ToList();
        Assert.Single(changed);
        Assert.Contains("\"input\": \"5HK\"", changed[0]);
    }

    [Fact]
    public void AVerbEditRecompilesThePoses()
    {
        var doc = Open();
        doc.Select("5HK");
        var lean = doc.Verbs.ToList().IndexOf("lean:-24");
        var spineBefore = doc.Compiled!.Track.At(doc.Compiled.FirstActiveFrame).Spine;

        doc.SetVerb(lean, "lean:-20");

        Assert.True(doc.CanSave, string.Join("; ", doc.ProblemsHere));
        Assert.NotEqual(spineBefore, doc.Compiled!.Track.At(doc.Compiled.FirstActiveFrame).Spine);
        AssertChecks(doc);
    }

    // Snapping home in recovery whips the arm round faster than the body allows: the check names
    // the move and the frame, and the set can't be saved until it's fixed.
    [Fact]
    public void AnEditThatFailsTheChecksIsShownAndBlocksSave()
    {
        var doc = Open();
        doc.Select("5LP");
        var recovery = doc.Verbs.ToList().FindIndex(v => MoveDocument.PhaseOf(v) == "recovery");
        doc.SetVerb(recovery, "recovery: ease:snap");

        var problem = Assert.Single(doc.ProblemsHere);
        Assert.Equal("5LP", problem.Move);
        Assert.Equal(5, problem.Frame);
        Assert.Contains("front elbow turns", problem.Text);
        Assert.False(doc.CanSave);
        Assert.Null(doc.SaveFiles());

        doc.SetVerb(recovery, "recovery: hold:0.2");
        Assert.True(doc.CanSave);
        Assert.Equal(Read("data/moves/placeholder.scripts.json"), doc.SaveFiles()!["data/moves/placeholder.scripts.json"]);
    }

    // A verb the compiler can't read is an error, shown against the move, and blocks save; the
    // last good compile is still there to show.
    [Fact]
    public void AVerbThatDoesntCompileBlocksSave()
    {
        var doc = Open();
        doc.Select("5MP");
        doc.SetVerb(0, "startup: pose:windup-punnch");

        Assert.NotNull(doc.ErrorHere);
        Assert.Contains("windup-punnch", doc.ErrorHere);
        Assert.False(doc.CanSave);
        Assert.NotNull(doc.Compiled);
    }

    // The rule matches the baseline the sim's own tests measure.
    [Theory]
    [InlineData("5LP", 6, 3)]
    [InlineData("5HK", 0, -4)]
    [InlineData("2HP", -4, -9)]
    [InlineData("2HK", null, -7)]
    public void AdvantageIsTheBaselines(string input, int? hit, int block)
    {
        var doc = Open();
        doc.Select(input);
        Assert.Equal((hit, (int?)block), MoveDocument.Advantage(doc.Compiled!));
    }
}
