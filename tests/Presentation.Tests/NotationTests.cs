using System;
using System.Linq;
using StickFighter.UI;
using Xunit;

namespace StickFighter.Presentation.Tests;

public class NotationTests
{
    [Fact]
    public void EveryMoveTheCharacterHasReadsAsGlyphs()
    {
        foreach (var move in Sim.Characters.Characters.Placeholder.Moves)
        {
            var glyphs = Notation.Parse(move.Input);
            Assert.Contains(glyphs, g => g.Kind == GlyphKind.Button);
        }
    }

    [Fact]
    public void AMotionIsItsDirectionsThenTheButton()
    {
        var glyphs = Notation.Parse("236P");
        Assert.Equal(new[] { 2, 3, 6 }, glyphs.Where(g => g.Kind == GlyphKind.Direction).Select(g => g.Direction));
        Assert.Equal(new Glyph(GlyphKind.Button, Attack: Attack.Punch, Strength: Strength.Any), glyphs[^1]);
    }

    [Theory]
    [InlineData("5LP", 5, Attack.Punch, Strength.Light)]
    [InlineData("2MK", 2, Attack.Kick, Strength.Medium)]
    [InlineData("6HP", 6, Attack.Punch, Strength.Heavy)]
    public void NormalsAreADirectionAndAStrength(string input, int direction, Attack attack, Strength strength)
    {
        var glyphs = Notation.Parse(input);
        Assert.Equal(new Glyph(GlyphKind.Direction, direction), glyphs[0]);
        Assert.Equal(new Glyph(GlyphKind.Button, Attack: attack, Strength: strength), glyphs[1]);
    }

    [Fact]
    public void JumpingAndTogetherAreMarked()
    {
        Assert.Equal(GlyphKind.Air, Notation.Parse("j.HK")[0].Kind);
        Assert.Equal(new[] { GlyphKind.Button, GlyphKind.Plus, GlyphKind.Button }, Notation.Parse("LP+LK").Select(g => g.Kind));
    }

    [Theory]
    [InlineData(1, -1, 1)]
    [InlineData(2, 0, 1)]
    [InlineData(3, 1, 1)]
    [InlineData(4, -1, 0)]
    [InlineData(5, 0, 0)]
    [InlineData(6, 1, 0)]
    [InlineData(7, -1, -1)]
    [InlineData(8, 0, -1)]
    [InlineData(9, 1, -1)]
    public void DirectionsPointTheWayTheNumpadDoes(int direction, int x, int y)
    {
        Assert.Equal((x, y), Notation.Vector(direction));
    }

    [Theory]
    [InlineData("236")]
    [InlineData("5XP")]
    [InlineData("LPLK")]
    [InlineData("")]
    public void NonsenseIsRejected(string input)
    {
        Assert.Throws<FormatException>(() => Notation.Parse(input));
    }
}
