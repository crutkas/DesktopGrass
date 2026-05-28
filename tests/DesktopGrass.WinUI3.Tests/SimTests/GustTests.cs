// GustTests.cs — §8 cursor-driven gust impulse.

using System;
using DesktopGrass.WinUI3;
using Xunit;

namespace DesktopGrass.WinUI3.Tests.SimTests;

public class GustTests
{
    private static Sim MakeSim()
    {
        // Use a window height that puts the gust band at the bottom edge
        // exactly where the renderer expects it: groundY = stripHeight +
        // headroom; cursor.y in [groundY - stripHeight - headroom, groundY]
        // is what's considered "in the band".
        var sim = new Sim(Constants.StripHeight + Constants.Headroom);
        sim.Generate(Constants.CanonicalTestSeed, 1920.0, 1.0);
        return sim;
    }

    private const double InBandY = 80.0;   // groundY = 110, band = [0, 110]

    [Fact]
    public void FirstEventOnlyPrimesCursorBaseline()
    {
        var sim = MakeSim();
        var blades0 = (Blade[])sim.Blades.Clone();
        sim.TestApplyCursorMove(new InputEvent(InputEventType.Move, 500.0, InBandY, 1.0));
        Assert.Equal(500.0, sim.TestPrevCursorX);
        Assert.Equal(1.0, sim.TestPrevCursorTime);
        for (int i = 0; i < sim.Blades.Length; i++)
            Assert.Equal(blades0[i].GustVelocity, sim.Blades[i].GustVelocity);
    }

    [Fact]
    public void EventOutsideGustBandDoesNotImpulse()
    {
        var sim = MakeSim();
        // First event primes baseline (regardless of band).
        sim.TestApplyCursorMove(new InputEvent(InputEventType.Move, 500.0, 200.0, 1.0));
        var blades0 = (Blade[])sim.Blades.Clone();
        // Subsequent out-of-band event with normal dt should not impulse.
        sim.TestApplyCursorMove(new InputEvent(InputEventType.Move, 700.0, 200.0, 1.05));
        for (int i = 0; i < sim.Blades.Length; i++)
            Assert.Equal(blades0[i].GustVelocity, sim.Blades[i].GustVelocity);
    }

    [Fact]
    public void BladesOutsideGustRadiusUnaffected()
    {
        var sim = MakeSim();
        sim.TestApplyCursorMove(new InputEvent(InputEventType.Move, 500.0, InBandY, 0.0));
        sim.TestApplyCursorMove(new InputEvent(InputEventType.Move, 700.0, InBandY, 0.05));
        foreach (ref readonly var b in sim.Blades.AsSpan())
        {
            double dx = Math.Abs(b.BaseX - 700.0);
            if (dx >= Constants.GustRadius)
                Assert.Equal(0.0, b.GustVelocity);
        }
    }

    [Fact]
    public void CenterBladeReceivesPeakImpulse()
    {
        var sim = MakeSim();
        sim.TestApplyCursorMove(new InputEvent(InputEventType.Move, 500.0, InBandY, 0.0));
        sim.TestApplyCursorMove(new InputEvent(InputEventType.Move, 700.0, InBandY, 0.05));

        // Find the blade closest to x=700.
        int bestIdx = 0;
        double bestDx = double.MaxValue;
        for (int i = 0; i < sim.Blades.Length; i++)
        {
            double dx = Math.Abs(sim.Blades[i].BaseX - 700.0);
            if (dx < bestDx) { bestDx = dx; bestIdx = i; }
        }

        // velX = (700 - 500) / 0.05 = 4000 DIP/sec (= MaxCursorSpeed)
        // impulseMagnitude = 4000 * 0.003 = 12.0 rad/sec
        // signDir = +1; smoothstep at this radius scales the impulse down.
        double t = 1.0 - bestDx / Constants.GustRadius;
        double smooth = t * t * (3.0 - 2.0 * t);
        double expected = 12.0 * smooth;

        Assert.Equal(expected, sim.Blades[bestIdx].GustVelocity, 9);
    }

    [Fact]
    public void NegativeVelocityProducesNegativeImpulse()
    {
        var sim = MakeSim();
        sim.TestApplyCursorMove(new InputEvent(InputEventType.Move, 700.0, InBandY, 0.0));
        sim.TestApplyCursorMove(new InputEvent(InputEventType.Move, 500.0, InBandY, 0.05));
        bool anyNegative = false;
        foreach (ref readonly var b in sim.Blades.AsSpan())
            if (b.GustVelocity < 0.0) { anyNegative = true; break; }
        Assert.True(anyNegative);
    }

    [Fact]
    public void CursorSpeedSaturatesAtMaxCursorSpeed()
    {
        var sim = MakeSim();
        // velX = (900 - 100) / 0.05 = 16000 DIP/sec, way over the
        // 4000 cap. Net impulse magnitude is capped at 4000 * 0.003 = 12.
        sim.TestApplyCursorMove(new InputEvent(InputEventType.Move, 100.0, InBandY, 0.0));
        sim.TestApplyCursorMove(new InputEvent(InputEventType.Move, 900.0, InBandY, 0.05));
        double max = 0.0;
        foreach (ref readonly var b in sim.Blades.AsSpan())
            max = Math.Max(max, Math.Abs(b.GustVelocity));
        Assert.InRange(max, 0.0, 12.0 + 1e-9);
    }

    [Fact]
    public void LongIdleResetsBaselineWithoutImpulse()
    {
        var sim = MakeSim();
        sim.TestApplyCursorMove(new InputEvent(InputEventType.Move, 100.0, InBandY, 0.0));
        sim.TestApplyCursorMove(new InputEvent(InputEventType.Move, 200.0, InBandY, 0.05));
        double[] before = new double[sim.Blades.Length];
        for (int i = 0; i < sim.Blades.Length; i++) before[i] = sim.Blades[i].GustVelocity;

        // Idle > CursorReinitGapSec triggers a baseline reset without
        // emitting an impulse for this single sample.
        sim.TestApplyCursorMove(new InputEvent(InputEventType.Move, 1500.0, InBandY, 1.10));

        for (int i = 0; i < sim.Blades.Length; i++)
            Assert.Equal(before[i], sim.Blades[i].GustVelocity);
        Assert.Equal(1500.0, sim.TestPrevCursorX);
        Assert.Equal(1.10, sim.TestPrevCursorTime);
    }

    [Fact]
    public void SmoothstepProducesContinuousFalloff()
    {
        // Belt-and-suspenders check on the §8 smoothstep falloff function.
        double prev = 1.0;
        for (double dx = 0.0; dx < Constants.GustRadius; dx += 5.0)
        {
            double t = 1.0 - dx / Constants.GustRadius;
            double smooth = t * t * (3.0 - 2.0 * t);
            Assert.InRange(smooth, 0.0, 1.0);
            Assert.True(smooth <= prev + 1e-12, $"non-monotonic at dx={dx}: {prev} -> {smooth}");
            prev = smooth;
        }
    }
}
