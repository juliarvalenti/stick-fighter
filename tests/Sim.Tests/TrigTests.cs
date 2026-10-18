using System;
using StickFighter.Sim.Numerics;
using Xunit;

namespace StickFighter.Sim.Tests;

public class TrigTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(256, 25080)]     // 22.5°
    [InlineData(512, 46341)]     // 45°
    [InlineData(1024, 65536)]    // 90°
    [InlineData(2048, 0)]        // 180°
    [InlineData(3072, -65536)]   // 270°
    [InlineData(3584, -46341)]   // 315°
    public void KnownSines(int units, long raw)
    {
        Assert.Equal(raw, Trig.Sin(Angle.FromUnits(units)).Raw);
    }

    [Fact]
    public void MatchesRoundedSineEverywhere()
    {
        for (var units = 0; units < Angle.UnitsPerTurn; units++)
        {
            var expected = (long)System.Math.Round(
                System.Math.Sin(units * 2 * System.Math.PI / Angle.UnitsPerTurn) * Fix.OneRaw,
                MidpointRounding.AwayFromZero);
            var actual = Trig.Sin(Angle.FromUnits(units)).Raw;
            Assert.True(System.Math.Abs(expected - actual) <= 1, $"sin({units}) = {actual}, expected {expected}");
        }
    }

    // Pins every table value. If this changes, determinism across builds or platforms broke.
    [Fact]
    public void TableChecksumIsPinned()
    {
        ulong hash = 14695981039346656037;
        for (var units = 0; units < Angle.UnitsPerTurn; units++)
        {
            hash ^= (ulong)Trig.Sin(Angle.FromUnits(units)).Raw;
            hash *= 1099511628211;
        }
        Assert.Equal(PinnedTableHash, hash);
    }

    private const ulong PinnedTableHash = 17851661922625093637;

    [Fact]
    public void CosIsSineShiftedAQuarter()
    {
        Assert.Equal(Fix.One, Trig.Cos(Angle.Zero));
        Assert.Equal(Fix.Zero, Trig.Cos(Angle.FromDegrees(90)));
        Assert.Equal(-Fix.One, Trig.Cos(Angle.FromDegrees(180)));
    }

    [Fact]
    public void RotatesAQuarterTurn()
    {
        var v = new FixVec2(Fix.FromInt(10), Fix.Zero);
        Assert.Equal(new FixVec2(Fix.Zero, Fix.FromInt(10)), Trig.Rotate(v, Angle.FromDegrees(90)));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(90, 1024)]
    [InlineData(-90, 3072)]
    [InlineData(45, 512)]
    [InlineData(1, 11)]
    [InlineData(360, 0)]
    [InlineData(450, 1024)]
    public void DegreesConvertToUnits(int degrees, int units)
    {
        Assert.Equal(units, Angle.FromDegrees(degrees).Units);
    }

    [Fact]
    public void MirrorFlipsAcrossVertical()
    {
        Assert.Equal(Angle.FromDegrees(180), Angle.Zero.MirrorX());
        Assert.Equal(Angle.FromDegrees(135), Angle.FromDegrees(45).MirrorX());
        Assert.Equal(Angle.FromDegrees(90), Angle.FromDegrees(90).MirrorX());
    }
}
