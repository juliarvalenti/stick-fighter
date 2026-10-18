using System;
using System.Collections.Generic;
using System.Text.Json;
using StickFighter.Sim.Characters;

namespace StickFighter.MoveCompiler;

// The whole pipeline: compile every script, load the result exactly as the game will, and run the
// body checks on every frame through the sim's own skeleton.
public static class Build
{
    public static (string Output, List<string> Problems) Run(string characterJson, string libraryJson, string scriptsJson, string name)
    {
        var baseCharacter = CharacterLoader.Load(characterJson, null, $"{name}.json");
        using var library = JsonDocument.Parse(libraryJson);
        using var scripts = JsonDocument.Parse(scriptsJson);

        var compiler = new Compiler(baseCharacter, library.RootElement);
        var moves = new List<CompiledMove>();
        foreach (var script in scripts.RootElement.GetProperty("moves").EnumerateArray())
            moves.Add(compiler.Compile(script.Clone()));
        var output = Output.Write(moves);

        var character = CharacterLoader.Load(characterJson, output, $"{name}.moves.json");
        var problems = new List<string>();
        foreach (var move in moves)
        {
            var track = character.FindMove(move.Input)?.Track
                ?? throw new InvalidOperationException($"compiled move {move.Input} didn't load");
            var keys = new List<PoseKey>();
            for (var frame = 0; frame < track.Length; frame++)
                keys.Add(track.At(frame));
            problems.AddRange(Checks.Run(move, keys, character));
        }
        return (output, problems);
    }
}
