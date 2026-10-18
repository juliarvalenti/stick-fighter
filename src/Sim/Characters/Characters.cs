using System.IO;

namespace StickFighter.Sim.Characters;

// The character data shipped with this build. The JSON files are embedded in the Sim assembly,
// so tests, tools, replays and the game all load exactly the same bytes, with no file paths.
public static class Characters
{
    public static readonly Character Placeholder = LoadEmbedded("placeholder");

    public static Character LoadEmbedded(string name)
    {
        var resource = $"characters/{name}.json";
        using var stream = typeof(Characters).Assembly.GetManifestResourceStream(resource)
            ?? throw new FileNotFoundException($"No embedded character data '{resource}'");
        using var reader = new StreamReader(stream);
        return CharacterLoader.Load(reader.ReadToEnd(), resource);
    }
}
