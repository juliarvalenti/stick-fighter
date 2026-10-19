using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace StickFighter.Editor;

// Writes move scripts in data/moves/<name>.scripts.json's own layout, so a save with nothing changed
// gives back the file byte for byte and an edit shows in a diff as the lines it touched: a move's
// fields on one line, then its verbs, a new line at each phase, and a blank line between moves.
public static class ScriptWriter
{
    private const string VerbIndent = "             ";

    private static readonly JsonSerializerOptions Strings = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static string Write(string comment, IEnumerable<JsonObject> moves)
    {
        var text = new StringBuilder();
        text.Append("{\n  \"comment\": ").Append(Str(comment)).Append(",\n  \"moves\": [\n");
        text.Append(string.Join(",\n\n", moves.Select(Move)));
        text.Append("\n  ]\n}\n");
        return text.ToString();
    }

    private static string Move(JsonObject move)
    {
        var fields = move.Where(p => p.Key != "do").Select(p => $"{Str(p.Key)}: {Value(p.Value)}");
        var text = new StringBuilder("    { ").Append(string.Join(", ", fields));
        if (move["do"] is JsonArray verbs)
            text.Append(",\n      \"do\": [").Append(Verbs(verbs)).Append(']');
        return text.Append(" }").ToString();
    }

    // A new line wherever a phase starts, after the first verb.
    private static string Verbs(JsonArray verbs)
    {
        var lines = new List<List<string>>();
        foreach (var node in verbs)
        {
            var verb = node!.GetValue<string>();
            if (lines.Count == 0 || MoveDocument.PhaseOf(verb) is not null)
                lines.Add(new List<string>());
            lines[^1].Add(Str(verb));
        }
        return string.Join(",\n" + VerbIndent, lines.Select(line => string.Join(", ", line)));
    }

    // Numbers and flags as they were written (a loaded value keeps its own text); strings escaped
    // only as JSON needs.
    private static string Value(JsonNode? node) => node switch
    {
        null => "null",
        JsonValue v when v.TryGetValue<string>(out var s) => Str(s),
        JsonArray a => "[" + string.Join(", ", a.Select(Value)) + "]", // as written: "chains": ["5LP", "2LP"]
        _ => node.ToJsonString(Strings),
    };

    private static string Str(string s) => JsonSerializer.Serialize(s, Strings);
}
