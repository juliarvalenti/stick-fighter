using StickFighter.Sim.Numerics;
using Xunit;

namespace StickFighter.Sim.Tests;

public class FixTests
{
    [Fact]
    public void ScaleIsSixteenSixteen()
    {
        Assert.Equal(65536, Fix.One.Raw);
        Assert.Equal(3 * 65536, Fix.FromInt(3).Raw);
        Assert.Equal(98304, Fix.FromRatio(3, 2).Raw);
    }

    [Theory]
    [InlineData(3, 2, 7, 4, "2.625")]       // 1.5 × 1.75
    [InlineData(-3, 2, 7, 4, "-2.625")]
    [InlineData(1, 3, 3, 1, "0.9999847412109375")] // 0.33333 × 3 keeps the floor of 1/3
    public void MultipliesExactly(long an, long ad, long bn, long bd, string expected)
    {
        Assert.Equal(expected, (Fix.FromRatio(an, ad) * Fix.FromRatio(bn, bd)).ToString());
    }

    [Fact]
    public void MultipliesLargeValuesWithoutOverflow()
    {
        var a = Fix.FromInt(1_000_000);
        Assert.Equal(Fix.FromInt(1_000_000) * 1_000_000, a * a);
    }

    [Theory]
    [InlineData(7, 2, "3.5")]
    [InlineData(-7, 2, "-3.5")]
    [InlineData(1, 3, "0.3333282470703125")]
    [InlineData(-1, 3, "-0.333343505859375")] // floors toward negative infinity
    public void DividesWithFloor(int a, int b, string expected)
    {
        Assert.Equal(expected, (Fix.FromInt(a) / Fix.FromInt(b)).ToString());
    }

    [Theory]
    [InlineData(5, 2, 2)]
    [InlineData(-5, 2, -3)]
    [InlineData(-1, 65536, -1)]
    public void FloorToIntFloors(long numerator, long denominator, int expected)
    {
        Assert.Equal(expected, Fix.FromRatio(numerator, denominator).FloorToInt());
    }

    [Theory]
    [InlineData(5, 2, 3)]
    [InlineData(-5, 2, -2)]
    [InlineData(7, 3, 2)]
    public void RoundToIntRoundsHalfUp(long numerator, long denominator, int expected)
    {
        Assert.Equal(expected, Fix.FromRatio(numerator, denominator).RoundToInt());
    }

    [Theory]
    [InlineData(4, "2")]
    [InlineData(2, "1.4141998291015625")]
    [InlineData(0, "0")]
    [InlineData(1_000_000, "1000")]
    public void SqrtIsFloorOfExactRoot(int value, string expected)
    {
        Assert.Equal(expected, Fix.Sqrt(Fix.FromInt(value)).ToString());
    }

    [Fact]
    public void ComparesAndClamps()
    {
        var lo = Fix.FromInt(-2);
        var hi = Fix.FromInt(5);
        Assert.True(lo < hi);
        Assert.Equal(hi, Fix.Clamp(Fix.FromInt(9), lo, hi));
        Assert.Equal(lo, Fix.Clamp(Fix.FromInt(-9), lo, hi));
        Assert.Equal(Fix.FromInt(2), Fix.Abs(Fix.FromInt(-2)));
        Assert.Equal(-1, Fix.Sign(lo));
    }

    [Theory]
    [InlineData(65536 + 32768, "1.5")]
    [InlineData(1, "0.0000152587890625")]
    [InlineData(-1, "-0.0000152587890625")]
    public void PrintsExactDecimal(long raw, string expected)
    {
        Assert.Equal(expected, Fix.FromRaw(raw).ToString());
    }
}
