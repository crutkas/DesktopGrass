using System.Numerics;
using Xunit;

namespace DesktopGrass.Win2D.Tests;

public sealed class DayTintTests
{
    private readonly record struct ExpectedPhase(float StartHour, byte R, byte G, byte B, byte Alpha);

    [Fact]
    public void PhasesArePinnedToSpec()
    {
        var expected = new[]
        {
            new ExpectedPhase( 0.0f,  40,  50,  90, 36),
            new ExpectedPhase( 4.0f,  60,  70, 110, 32),
            new ExpectedPhase( 6.0f, 255, 180, 140, 28),
            new ExpectedPhase( 8.0f, 255, 220, 160, 16),
            new ExpectedPhase(10.0f, 255, 255, 255,  0),
            new ExpectedPhase(17.0f, 240, 170, 110, 22),
            new ExpectedPhase(19.0f, 220, 110,  90, 30),
            new ExpectedPhase(20.0f,  90,  80, 130, 28),
            new ExpectedPhase(22.0f,  40,  50,  90, 36),
        };

        Assert.Equal(expected.Length, Constants.DAYTINT_PHASES.Length);
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i].StartHour, Constants.DAYTINT_PHASES[i].StartHour);
            Assert.Equal(expected[i].R, Constants.DAYTINT_PHASES[i].R);
            Assert.Equal(expected[i].G, Constants.DAYTINT_PHASES[i].G);
            Assert.Equal(expected[i].B, Constants.DAYTINT_PHASES[i].B);
            Assert.Equal(expected[i].Alpha, Constants.DAYTINT_PHASES[i].Alpha);
        }
    }

    [Fact]
    public void NoonIsTransparent()
    {
        (_, _, _, int alpha) = ComputeBytes(12.0);
        Assert.Equal(0, alpha);
    }

    [Fact]
    public void NightStartsExactlyAt22()
    {
        AssertTint(22.0, 40, 50, 90, 36);
    }

    [Fact]
    public void SevenInterpolatesSunriseToMorning()
    {
        (int r, int g, int b, int a) = ComputeBytes(7.0);

        Assert.InRange(r, 254, 256);
        Assert.InRange(g, 199, 201);
        Assert.InRange(b, 149, 151);
        Assert.InRange(a, 21, 23);
    }

    [Fact]
    public void TwentyThreeRemainsNight()
    {
        AssertTint(23.0, 40, 50, 90, 36);
    }

    [Fact]
    public void ZeroAndTwentyFourWrapEquivalently()
    {
        var zero = ComputeBytes(0.0);
        var twentyFour = ComputeBytes(24.0);
        var beforeMidnight = ComputeBytes(-0.0001 + 24.0);

        Assert.Equal(zero, twentyFour);
        Assert.Equal(zero, beforeMidnight);
    }

    [Fact]
    public void AlphaNeverExceedsMaximum()
    {
        for (int i = 0; i <= 239; i++)
        {
            (_, _, _, int alpha) = ComputeBytes(i / 10.0);
            Assert.True(alpha <= Constants.DAYTINT_MAX_ALPHA, $"alpha {alpha} at hour {i / 10.0}");
        }

        (_, _, _, int finalAlpha) = ComputeBytes(23.99);
        Assert.True(finalAlpha <= Constants.DAYTINT_MAX_ALPHA);
    }

    [Fact]
    public void BoundaryAt8IsMorning()
    {
        AssertTint(8.0, 255, 220, 160, 16);
    }

    private static void AssertTint(double hour, int expectedR, int expectedG, int expectedB, int expectedA)
    {
        (int r, int g, int b, int a) = ComputeBytes(hour);
        Assert.Equal(expectedR, r);
        Assert.Equal(expectedG, g);
        Assert.Equal(expectedB, b);
        Assert.Equal(expectedA, a);
    }

    private static (int R, int G, int B, int A) ComputeBytes(double hour)
    {
        Vector4 tint = Constants.ComputeDayTint(hour);
        return (ToByte(tint.X), ToByte(tint.Y), ToByte(tint.Z), ToByte(tint.W));
    }

    private static int ToByte(float channel) => (int)MathF.Round(channel * 255.0f);
}
