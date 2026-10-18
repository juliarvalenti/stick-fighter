// Compiles move scripts into the character's compiled move data.
//
//   dotnet run --project tools/MoveCompiler              compile and write data/characters/<name>.moves.json,
//                                                        and the breathing stand and crouch in <name>.json
//   dotnet run --project tools/MoveCompiler -- --check   fail if either committed file is out of date (CI)
//
// Inputs: data/characters/<name>.json (skeleton, stance poses), data/moves/library.json (key poses)
// and data/moves/<name>.scripts.json (the scripts). Every compiled move must pass the body checks.

using System;
using System.IO;
using StickFighter.MoveCompiler;

const string CharacterName = "placeholder";
var check = Array.IndexOf(args, "--check") >= 0;
var root = FindRoot();

var characterPath = Path.Combine(root, "data", "characters", $"{CharacterName}.json");
var outputPath = Path.Combine(root, "data", "characters", $"{CharacterName}.moves.json");
var characterJson = File.ReadAllText(characterPath);

try
{
    var (output, problems) = Build.Run(
        characterJson,
        File.ReadAllText(Path.Combine(root, "data", "moves", "library.json")),
        File.ReadAllText(Path.Combine(root, "data", "moves", $"{CharacterName}.scripts.json")),
        CharacterName);
    foreach (var problem in problems)
        Console.WriteLine($"FAIL {problem}");
    if (problems.Count > 0)
        return 1;

    // The stand and crouch tracks breathe; they're generated into the character file itself.
    var breathing = Breathing.Apply(characterJson);

    var relative = Path.GetRelativePath(root, outputPath);
    var characterRelative = Path.GetRelativePath(root, characterPath);
    if (check)
    {
        var committed = File.Exists(outputPath) ? File.ReadAllText(outputPath) : "";
        var stale = false;
        if (committed != output)
        {
            Console.WriteLine($"FAIL {relative} is out of date with the move scripts.");
            stale = true;
        }
        if (characterJson != breathing)
        {
            Console.WriteLine($"FAIL {characterRelative}'s stand and crouch breathing is out of date with its first keys.");
            stale = true;
        }
        if (stale)
        {
            Console.WriteLine("     Run: dotnet run --project tools/MoveCompiler   and commit the result.");
            return 1;
        }
        Console.WriteLine($"ok   {relative} matches the scripts, and {characterRelative} breathes");
        return 0;
    }

    File.WriteAllText(outputPath, output);
    Console.WriteLine($"wrote {relative}");
    if (characterJson != breathing)
    {
        File.WriteAllText(characterPath, breathing);
        Console.WriteLine($"wrote the stand and crouch breathing into {characterRelative}");
    }
    return 0;
}
catch (CompileException e)
{
    Console.WriteLine($"FAIL {e.Message}");
    return 1;
}

static string FindRoot()
{
    var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "StickFighter.sln")))
        dir = dir.Parent;
    return dir?.FullName ?? throw new DirectoryNotFoundException("Run from inside the repo");
}
