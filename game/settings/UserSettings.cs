using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace StickFighter.Settings;

// Everything the options menu sets, saved to user://settings.cfg between runs. Presentation and
// config only: nothing here enters GameState. Input delay is stored for Rollback, which will apply
// it; the volumes are stored for Sound.
public sealed class UserSettings
{
    public const int MaxInputDelay = 4;

    public int MusicVolume { get; set; } = 80; // 0 to 100
    public int EffectsVolume { get; set; } = 80;
    public int AnnouncerVolume { get; set; } = 80;
    public int InputDelay { get; set; } // frames, 0 to MaxInputDelay
    public bool Fullscreen { get; set; }
    public bool Vsync { get; set; } = true;
    public Bindings Bindings { get; }

    public UserSettings(Bindings defaults) => Bindings = new Bindings(defaults);

    // A small INI: [section] lines, then key=value. Anything missing or unreadable keeps its default,
    // so an old or hand-edited file never stops the game starting.
    public string Write()
    {
        var text = new StringBuilder();
        text.Append("[audio]\n");
        text.Append($"music={MusicVolume}\neffects={EffectsVolume}\nannouncer={AnnouncerVolume}\n");
        text.Append("\n[input]\n");
        text.Append($"delay={InputDelay}\n");
        text.Append("\n[display]\n");
        text.Append($"fullscreen={(Fullscreen ? "true" : "false")}\nvsync={(Vsync ? "true" : "false")}\n");
        for (var p = 0; p < 2; p++)
        {
            text.Append($"\n[p{p + 1}.keys]\n");
            foreach (var binding in Bindings.All)
                text.Append(CultureInfo.InvariantCulture, $"{binding}={Bindings.Key(p, binding)}\n");
            text.Append($"\n[p{p + 1}.pad]\n");
            foreach (var binding in Bindings.All)
            {
                if (Bindings.Pad(p, binding) is { } pad)
                    text.Append($"{binding}={pad}\n");
            }
        }
        return text.ToString();
    }

    public static UserSettings Read(string text, Bindings defaults)
    {
        var settings = new UserSettings(defaults);
        var section = "";
        var padsSeen = new HashSet<int>();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                section = line[1..^1];
                continue;
            }
            var eq = line.IndexOf('=', StringComparison.Ordinal);
            if (eq <= 0)
                continue;
            var key = line[..eq].Trim();
            var value = line[(eq + 1)..].Trim();
            settings.Apply(section, key, value, padsSeen);
        }
        return settings;
    }

    private void Apply(string section, string key, string value, HashSet<int> padsSeen)
    {
        switch (section, key)
        {
            case ("audio", "music"):
                MusicVolume = Percent(value, MusicVolume);
                return;
            case ("audio", "effects"):
                EffectsVolume = Percent(value, EffectsVolume);
                return;
            case ("audio", "announcer"):
                AnnouncerVolume = Percent(value, AnnouncerVolume);
                return;
            case ("input", "delay"):
                InputDelay = int.TryParse(value, out var delay) ? Math.Clamp(delay, 0, MaxInputDelay) : InputDelay;
                return;
            case ("display", "fullscreen"):
                Fullscreen = value == "true";
                return;
            case ("display", "vsync"):
                Vsync = value != "false";
                return;
        }

        // [p1.keys] / [p2.pad] and so on.
        if (section.Length < 4 || section[0] != 'p' || !Enum.TryParse<Binding>(key, out var binding))
            return;
        var player = section[1] - '1';
        if (player is not (0 or 1))
            return;
        if (section.EndsWith(".keys", StringComparison.Ordinal) && long.TryParse(value, out var code))
        {
            Bindings.SetKey(player, binding, code);
        }
        else if (section.EndsWith(".pad", StringComparison.Ordinal) && PadInput.Parse(value) is { } pad)
        {
            // The file lists a player's whole pad, so its first line replaces the defaults.
            if (padsSeen.Add(player))
                Bindings.ClearPad(player);
            Bindings.SetPad(player, binding, pad);
        }
    }

    private static int Percent(string value, int fallback) =>
        int.TryParse(value, out var n) ? Math.Clamp(n, 0, 100) : fallback;
}
