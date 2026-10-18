using System.IO;

namespace StickFighter.Sim.Characters;

// The character data shipped with this build. The JSON files are embedded in the Sim assembly,
// so tests, tools, replays and the game all load exactly the same bytes, with no file paths.
// Each character is <name>.json plus its compiled moves, <name>.moves.json.
public static class Characters
{
    public static readonly Character Placeholder = LoadEmbedded("placeholder");

    public static Character LoadEmbedded(string name)
    {
        var resource = $"characters/{name}.json";
        return CharacterLoader.Load(
            Read(resource) ?? throw new FileNotFoundException($"No embedded character data '{resource}'"),
            Read($"characters/{name}.moves.json"),
            resource);
    }

    private static string? Read(string resource)
    {
        using var stream = typeof(Characters).Assembly.GetManifestResourceStream(resource);
        if (stream is null)
            return null;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
