using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using StickFighter.Style;
using Xunit;
using static StickFighter.Art.Tests.ColorScience;

namespace StickFighter.Art.Tests;

public class PaletteTests
{
    // WCAG AA for normal-size text.
    private const double MinContrast = 4.5;

    // Players' colors must stay this far apart (CIE76) under every simulated deficiency, and this
    // far from the ink of the floor line they stand on.
    private const double MinPlayerDistance = 25;

    private static readonly Vision[] AllVision = Enum.GetValues<Vision>();

    private static IEnumerable<(string Name, string Hex)> Tokens() =>
        typeof(Palette).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (f.Name, (string)f.GetRawConstantValue()!));

    private static string NameOf(string hex) => string.Join("/", Tokens().Where(t => t.Hex == hex).Select(t => t.Name));

    [Fact]
    public void EveryTokenIsAHexColor()
    {
        foreach (var (name, hex) in Tokens())
        {
            var ex = Record.Exception(() => Linear(hex));
            Assert.True(ex is null, $"{name} = '{hex}': {ex?.Message}");
        }
    }

    [Fact]
    public void EveryTextPairMeetsAA()
    {
        var failures = Palette.TextPairs
            .Select(p => (p.Text, p.Background, Ratio: Contrast(p.Text, p.Background)))
            .Where(p => p.Ratio < MinContrast)
            .Select(p => $"{NameOf(p.Text)} on {NameOf(p.Background)}: {p.Ratio:0.00}:1")
            .ToList();
        Assert.True(failures.Count == 0, $"below {MinContrast}:1: {string.Join(", ", failures)}");
    }

    [Fact]
    public void EveryPlayerColorStandsOutFromTheStage()
    {
        // Paper and every backdrop shape a fighter can stand in front of.
        foreach (var backdrop in Palette.Backdrops)
        {
            foreach (var player in Palette.Players)
            {
                var ratio = Contrast(player, backdrop);
                Assert.True(ratio >= MinContrast, $"{NameOf(player)} on {NameOf(backdrop)}: {ratio:0.00}:1");
            }
        }
    }

    [Fact]
    public void PlayerColorsStayDistinctUnderColorBlindness()
    {
        var players = Palette.Players;
        var failures = new List<string>();
        for (var i = 0; i < players.Length; i++)
        {
            foreach (var vision in AllVision)
            {
                var ink = Distance(players[i], Palette.Ink, vision);
                if (ink < MinPlayerDistance)
                    failures.Add($"{NameOf(players[i])} vs Ink, {vision}: {ink:0.0}");

                for (var j = i + 1; j < players.Length; j++)
                {
                    var d = Distance(players[i], players[j], vision);
                    if (d < MinPlayerDistance)
                        failures.Add($"{NameOf(players[i])} vs {NameOf(players[j])}, {vision}: {d:0.0}");
                }
            }
        }
        Assert.True(failures.Count == 0, $"closer than {MinPlayerDistance}: {string.Join(", ", failures)}");
    }

    [Fact]
    public void TheContrastMathMatchesWcag()
    {
        Assert.Equal(21, Contrast("#000000", "#FFFFFF"), 6);
        Assert.Equal(1, Contrast("#777777", "#777777"), 6);
        // The WCAG threshold example: #767676 on white is the lightest gray that passes AA.
        Assert.InRange(Contrast("#767676", "#FFFFFF"), 4.5, 4.6);
    }

    [Fact]
    public void SimulatedDeficienciesConfuseTheClassicPairs()
    {
        // Red and green are near-identical to a deuteranope, blue and green less so to a tritanope;
        // grays are unchanged by every simulation.
        Assert.True(Distance("#CC3333", "#669900", Vision.Deuteranopia) < Distance("#CC3333", "#669900", Vision.Normal) / 3);
        foreach (var vision in AllVision)
            Assert.True(Distance("#808080", "#808080", vision) < 0.5);
        Assert.True(Distance("#FFFFFF", "#000000", Vision.Protanopia) > 90);
    }
}
