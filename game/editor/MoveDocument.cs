using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using StickFighter.MoveCompiler;
using StickFighter.Sim.Characters;

namespace StickFighter.Editor;

// A check the compiler failed: which move, which frame (if it's about one), and what.
public sealed record Problem(string Move, int? Frame, string Text);

// What the scripts compile to right now: the character to preview with, and anything wrong. A
// script that doesn't compile at all has an Error and no Character.
public sealed record CompileResult(Character? Character, string? MovesJson, string? Error, IReadOnlyList<Problem> Problems)
{
    public bool Ok => Error is null && Problems.Count == 0;
}

// The pose editor's model: a character's move scripts, opened for editing. Every edit recompiles
// the whole set through MoveCompiler in-process and runs its checks, as `MoveCompiler --check` does
// in CI; only a set with no problems can be saved. Saving gives the three files the CLI writes: the
// scripts (in their own layout), the compiled moves, and the character file (stand and crouch
// breathing). No Godot here, so it's tested in Presentation.Tests.
public sealed partial class MoveDocument
{
    public static readonly string[] Phases = { "startup", "active", "recovery" };
    public static readonly string[] Strengths = { "light", "medium", "heavy" };
    public static readonly string[] Guards = { "high", "mid", "low" };

    // The frame data the panel edits, in its order.
    public static readonly (string Key, string Label, int Min, int Max)[] Numbers =
    {
        ("startup", "Startup", 1, 60),
        ("active", "Active", 1, 30),
        ("recovery", "Recovery", 0, 90),
        ("damage", "Damage", 0, 400),
        ("stun", "Stun", 0, 400),
        ("pushbackHit", "Pushback on hit", 0, 60),
        ("pushbackBlock", "Pushback on block", 0, 60),
    };

    private readonly string _characterJson;
    private readonly string _libraryJson;
    private readonly string _name;
    private readonly string _comment;
    private readonly List<JsonObject> _moves;

    public MoveDocument(string characterJson, string libraryJson, string scriptsJson, string name = "placeholder")
    {
        _characterJson = characterJson;
        _libraryJson = libraryJson;
        _name = name;
        var root = JsonNode.Parse(scriptsJson)!.AsObject();
        _comment = root["comment"]?.GetValue<string>() ?? "";
        _moves = root["moves"]!.AsArray().Select(m => m!.AsObject()).ToList();
        Recompile();
    }

    public IReadOnlyList<string> Inputs => _moves.Select(m => m["input"]!.GetValue<string>()).ToList();

    public int Selected { get; private set; }

    public string Input => Inputs[Selected];

    public string Name => Script["name"]!.GetValue<string>();

    public CompileResult Result { get; private set; } = null!;

    // The last set that compiled, to keep showing while an edit doesn't.
    public Character? LastGood { get; private set; }

    public bool Changed { get; private set; }

    public bool CanSave => Result.Ok;

    private JsonObject Script => _moves[Selected];

    public void Select(int index) => Selected = Math.Clamp(index, 0, _moves.Count - 1);

    public void Select(string input) => Select(Inputs.ToList().IndexOf(input));

    public int GetNumber(string key) => Script[key]?.GetValue<int>() ?? 0;

    public void SetNumber(string key, int value)
    {
        var (_, _, min, max) = Numbers.First(n => n.Key == key);
        Script[key] = Math.Clamp(value, min, max);
        Edited();
    }

    public string GetText(string key) => Script[key]?.GetValue<string>() ?? "";

    // Strength and guard step through their values.
    public void Cycle(string key, int step)
    {
        var values = key == "strength" ? Strengths : Guards;
        var i = Array.IndexOf(values, GetText(key));
        Script[key] = values[((i + step) % values.Length + values.Length) % values.Length];
        Edited();
    }

    public IReadOnlyList<string> Verbs => Script["do"]!.AsArray().Select(v => v!.GetValue<string>()).ToList();

    public void SetVerb(int index, string text)
    {
        Script["do"]!.AsArray()[index] = text.Trim();
        Edited();
    }

    // The verb's phase prefix, if it starts one ("active: reach:..." is "active").
    public static string? PhaseOf(string verb)
    {
        var colon = verb.IndexOf(':');
        return colon > 0 && colon + 1 < verb.Length && verb[colon + 1] == ' ' && Phases.Contains(verb[..colon]) ? verb[..colon] : null;
    }

    // The open move as it compiles now (or last compiled), as the game would load it.
    public Move? Compiled => (Result.Character ?? LastGood)?.FindMove(Input);

    // Frame advantage on hit and on block, by the rule the sim's baseline tests and the training
    // analyser measure: the defender's stun minus the attacker's frames from the hit on (active and
    // recovery). None on hit for a knockdown (the defender is down, not in hitstun), none for a throw.
    public static (int? Hit, int? Block) Advantage(Move move) =>
        move.Kind == MoveKind.Throw
            ? (null, null)
            : (move.HardKnockdown ? null : move.Hitstun - move.Active - move.Recovery, move.Blockstun - move.Active - move.Recovery);

    // Problems with the open move, and the frames they name.
    public IEnumerable<Problem> ProblemsHere => Result.Problems.Where(p => p.Move == Input);

    public string? ErrorHere => Result.Error is { } e && (e.StartsWith($"move {Input}:", StringComparison.Ordinal) || !e.StartsWith("move ", StringComparison.Ordinal)) ? e : null;

    // The scripts as they'd be saved.
    public string ScriptsText() => ScriptWriter.Write(_comment, _moves);

    // The three files a save writes, keyed by their path under the repo; null if it can't be saved.
    public IReadOnlyDictionary<string, string>? SaveFiles()
    {
        if (!CanSave)
            return null;
        return new Dictionary<string, string>
        {
            [$"data/moves/{_name}.scripts.json"] = ScriptsText(),
            [$"data/characters/{_name}.moves.json"] = Result.MovesJson!,
            [$"data/characters/{_name}.json"] = Breathing.Apply(_characterJson),
        };
    }

    public void Saved() => Changed = false;

    private void Edited()
    {
        Changed = true;
        Recompile();
    }

    private void Recompile()
    {
        try
        {
            var (output, problems) = Build.Run(_characterJson, _libraryJson, ScriptsText(), _name);
            var character = CharacterLoader.Load(Breathing.Apply(_characterJson), output, $"{_name}.moves.json");
            LastGood = character;
            Result = new CompileResult(character, output, null, problems.Select(Parse).ToList());
        }
        catch (Exception e) // whatever an edit breaks, the editor shows it rather than going down
        {
            Result = new CompileResult(null, null, e.Message, Array.Empty<Problem>());
        }
    }

    private static Problem Parse(string text)
    {
        var m = ProblemPattern().Match(text);
        return m.Success
            ? new Problem(m.Groups[1].Value, m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : null, m.Groups[3].Value)
            : new Problem("", null, text);
    }

    [GeneratedRegex(@"^move (\S+)(?: frame (\d+))?: (.*)$")]
    private static partial Regex ProblemPattern();
}
