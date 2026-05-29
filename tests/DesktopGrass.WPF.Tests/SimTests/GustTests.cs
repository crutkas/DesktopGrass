// GustTests.cs - §8 cursor-driven gust impulse.

using System;
using DesktopGrass.WPF;
using Xunit;

namespace DesktopGrass.WPF.Tests.SimTests;

public class GustTests
{
    private static Sim MakeSim(double monitorWidth = 1920.0)
    {
        return new Sim
        {
            Blades = Sim.GenerateBlades(Constants.CANONICAL_TEST_SEED, monitorWidth, 1.0),
            GroundY = 110.0,
            WindowHeight = 110.0,
        };
    }

    [Fact]
    public void FirstEventOnlyPrimesCursorBaseline()
    {
        var sim = MakeSim();
        var blades0 = (Blade[])sim.Blades.Clone();
        sim.ApplyCursorMove(new InputEvent(EventType.Move, 500.0, 80.0, 1.0));
        Assert.Equal(500.0, sim.PrevCursorX);
        Assert.Equal(1.0, sim.PrevCursorTime);
        for (int i = 0; i < sim.Blades.Length; i++)
            Assert.Equal(blades0[i].GustVelocity, sim.Blades[i].GustVelocity);
    }

    [Fact]
    public void EventOutsideGustBandIgnored()
    {
        var sim = MakeSim();
        sim.ApplyCursorMove(new InputEvent(EventType.Move, 500.0, 200.0, 1.0));
        Assert.Equal(-1.0, sim.PrevCursorTime);
        sim.ApplyCursorMove(new InputEvent(EventType.Move, 500.0, -10.0, 1.0));
        Assert.Equal(-1.0, sim.PrevCursorTime);
    }

    [Fact]
    public void BladesOutsideGustRadiusUnaffected()
    {
        var sim = MakeSim();
        sim.ApplyCursorMove(new InputEvent(EventType.Move, 500.0, 80.0, 0.0));
        sim.ApplyCursorMove(new InputEvent(EventType.Move, 700.0, 80.0, 0.05));
        foreach (ref readonly var b in sim.Blades.AsSpan())
        {
            double dx = Math.Abs(b.BaseX - 700.0);
            if (dx >= Constants.GUST_RADIUS)
                Assert.Equal(0.0, b.GustVelocity);
        }
    }

    [Fact]
    public void CenterBladeReceivesPeakImpulse()
    {
        var sim = MakeSim();
        sim.ApplyCursorMove(new InputEvent(EventType.Move, 500.0, 80.0, 0.0));
        sim.ApplyCursorMove(new InputEvent(EventType.Move, 700.0, 80.0, 0.05));

        int bestIdx = 0;
        double bestDx = double.MaxValue;
        for (int i = 0; i < sim.Blades.Length; i++)
        {
            double dx = Math.Abs(sim.Blades[i].BaseX - 700.0);
            if (dx < bestDx) { bestDx = dx; bestIdx = i; }
        }

        double t = 1.0 - bestDx / Constants.GUST_RADIUS;
        double smooth = t * t * (3.0 - 2.0 * t);
        double expected = 12.0 * smooth;

        Assert.Equal(expected, sim.Blades[bestIdx].GustVelocity, 9);
    }

    [Fact]
    public void NegativeVelocityProducesNegativeImpulse()
    {
        var sim = MakeSim();
        sim.ApplyCursorMove(new InputEvent(EventType.Move, 700.0, 80.0, 0.0));
        sim.ApplyCursorMove(new InputEvent(EventType.Move, 500.0, 80.0, 0.05));
        bool anyNegative = false;
        foreach (ref readonly var b in sim.Blades.AsSpan())
            if (b.GustVelocity < 0.0) { anyNegative = true; break; }
        Assert.True(anyNegative);
    }

    [Fact]
    public void CursorSpeedSaturatesAtMaxCursorSpeed()
    {
        var simA = MakeSim();
        simA.ApplyCursorMove(new InputEvent(EventType.Move, 100.0, 80.0, 0.0));
        simA.ApplyCursorMove(new InputEvent(EventType.Move, 900.0, 80.0, 0.05));
        double maxA = 0.0;
        foreach (ref readonly var b in simA.Blades.AsSpan())
            maxA = Math.Max(maxA, Math.Abs(b.GustVelocity));
        Assert.InRange(maxA, 0.0, 12.0 + 1e-9);
    }

    [Fact]
    public void LongIdleResetsBaselineWithoutImpulse()
    {
        var sim = MakeSim();
        sim.ApplyCursorMove(new InputEvent(EventType.Move, 100.0, 80.0, 0.0));
        sim.ApplyCursorMove(new InputEvent(EventType.Move, 200.0, 80.0, 0.05));
        double[] before = new double[sim.Blades.Length];
        for (int i = 0; i < sim.Blades.Length; i++) before[i] = sim.Blades[i].GustVelocity;

        sim.ApplyCursorMove(new InputEvent(EventType.Move, 1500.0, 80.0, 1.10));

        for (int i = 0; i < sim.Blades.Length; i++)
            Assert.Equal(before[i], sim.Blades[i].GustVelocity);
        Assert.Equal(1500.0, sim.PrevCursorX);
        Assert.Equal(1.10, sim.PrevCursorTime);
    }

    [Fact]
    public void SmoothstepProducesContinuousFalloff()
    {
        double prev = 1.0;
        for (double dx = 0.0; dx < Constants.GUST_RADIUS; dx += 5.0)
        {
            double t = 1.0 - dx / Constants.GUST_RADIUS;
            double smooth = t * t * (3.0 - 2.0 * t);
            Assert.InRange(smooth, 0.0, 1.0);
            Assert.True(smooth <= prev + 1e-12, $"non-monotonic at dx={dx}: {prev} -> {smooth}");
            prev = smooth;
        }
    }
}

