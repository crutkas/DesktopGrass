// CutTests.cs - §9 cut state animation.

using System;
using DesktopGrass.WPF;
using Xunit;

namespace DesktopGrass.WPF.Tests.SimTests;

public class CutTests
{
    private static Sim MakeSim()
    {
        return new Sim
        {
            Blades = Sim.GenerateBlades(Constants.CANONICAL_TEST_SEED, 1920.0, 1.0),
            GroundY = 110.0,
            WindowHeight = 110.0,
        };
    }

    [Fact]
    public void ClickOutsideCutBandIgnored()
    {
        var sim = MakeSim();
        sim.ApplyClick(500.0, 10.0, 0.0);
        foreach (ref readonly var b in sim.Blades.AsSpan())
            Assert.Equal(-1.0, b.CutAnimStart);
        sim.ApplyClick(500.0, 200.0, 0.0);
        foreach (ref readonly var b in sim.Blades.AsSpan())
            Assert.Equal(-1.0, b.CutAnimStart);
    }

    [Fact]
    public void ClickInsideBandStartsAnimForBladesInRadius()
    {
        var sim = MakeSim();
        sim.ApplyClick(500.0, 80.0, 0.0);
        bool any = false;
        foreach (ref readonly var b in sim.Blades.AsSpan())
        {
            double dx = Math.Abs(b.BaseX - 500.0);
            if (dx < Constants.CUT_RADIUS)
            {
                Assert.True(b.CutAnimStart >= 0.0);
                any = true;
            }
            else
            {
                Assert.Equal(-1.0, b.CutAnimStart);
            }
        }
        Assert.True(any);
    }

    [Fact]
    public void CutHeightLerpsLinearlyToZeroOverDuration()
    {
        var sim = MakeSim();
        sim.GlobalTime = 0.0;
        sim.ApplyClick(500.0, 80.0, 0.0);

        int idx = -1;
        for (int i = 0; i < sim.Blades.Length; i++)
        {
            if (Math.Abs(sim.Blades[i].BaseX - 500.0) < Constants.CUT_RADIUS
                && sim.Blades[i].CutAnimStart >= 0.0)
            { idx = i; break; }
        }
        Assert.True(idx >= 0);

        double dt = 0.05;
        double[] heights = new double[6];
        heights[0] = sim.Blades[idx].CutHeight;
        for (int frame = 0; frame < 5; frame++)
        {
            sim.Tick(dt, ReadOnlySpan<InputEvent>.Empty);
            heights[frame + 1] = sim.Blades[idx].CutHeight;
        }

        Assert.Equal(1.0, heights[0], 9);
        Assert.Equal(0.75, heights[1], 9);
        Assert.Equal(0.5, heights[2], 9);
        Assert.Equal(0.25, heights[3], 9);
        Assert.Equal(0.0, heights[4], 9);
        Assert.Equal(0.0, heights[5], 9);
    }

    [Fact]
    public void RepeatedClickOnAnimatingBladeIsNoOp()
    {
        var sim = MakeSim();
        sim.ApplyClick(500.0, 80.0, 0.0);
        int idx = -1;
        for (int i = 0; i < sim.Blades.Length; i++)
            if (sim.Blades[i].CutAnimStart >= 0.0) { idx = i; break; }
        Assert.True(idx >= 0);
        double originalStart = sim.Blades[idx].CutAnimStart;
        double originalInitial = sim.Blades[idx].CutInitialHeight;

        sim.Tick(0.05, ReadOnlySpan<InputEvent>.Empty);
        sim.ApplyClick(500.0, 80.0, sim.GlobalTime);

        Assert.Equal(originalStart, sim.Blades[idx].CutAnimStart);
        Assert.Equal(originalInitial, sim.Blades[idx].CutInitialHeight);
    }

    [Fact]
    public void ClickOnFullyCutBladeIsNoOp()
    {
        var sim = MakeSim();
        sim.GlobalTime = 0.0;
        sim.ApplyClick(500.0, 80.0, 0.0);
        for (int i = 0; i < 5; i++) sim.Tick(0.05, ReadOnlySpan<InputEvent>.Empty);

        int idx = -1;
        for (int i = 0; i < sim.Blades.Length; i++)
        {
            if (Math.Abs(sim.Blades[i].BaseX - 500.0) < Constants.CUT_RADIUS
                && sim.Blades[i].CutHeight == 0.0)
            { idx = i; break; }
        }
        Assert.True(idx >= 0);

        sim.ApplyClick(500.0, 80.0, sim.GlobalTime);
        Assert.Equal(-1.0, sim.Blades[idx].CutAnimStart);
        Assert.Equal(0.0, sim.Blades[idx].CutHeight);
    }

    [Fact]
    public void CutLeavesOutOfRadiusBladesUntouched()
    {
        var sim = MakeSim();
        sim.ApplyClick(500.0, 80.0, 0.0);
        foreach (ref readonly var b in sim.Blades.AsSpan())
        {
            double dx = Math.Abs(b.BaseX - 500.0);
            if (dx >= Constants.CUT_RADIUS)
            {
                Assert.Equal(1.0, b.CutHeight);
                Assert.Equal(-1.0, b.CutAnimStart);
            }
        }
    }

    [Fact]
    public void StumpStrokeProducedForVeryCutBlade()
    {
        var b = new Blade
        {
            BaseX = 100.0,
            Height = 30.0,
            Thickness = 1.5,
            Hue = 2,
            CutHeight = 0.02,
            EffectiveLean = 5.0,
        };
        var stroke = Sim.ComputeBladeStroke(b, groundY: 110.0);
        Assert.Equal(100.0, stroke.BaseX);
        Assert.Equal(110.0, stroke.BaseY);
        Assert.Equal(100.0, stroke.TipX);
        Assert.Equal(108.0, stroke.TipY);
    }

    [Fact]
    public void UncutBladeStrokeUsesEffectiveLean()
    {
        var b = new Blade
        {
            BaseX = 100.0,
            Height = 30.0,
            Thickness = 1.5,
            Hue = 2,
            CutHeight = 1.0,
            EffectiveLean = 5.0,
        };
        var stroke = Sim.ComputeBladeStroke(b, groundY: 110.0);
        Assert.Equal(100.0, stroke.BaseX);
        Assert.Equal(110.0, stroke.BaseY);
        Assert.Equal(105.0, stroke.TipX, 9);
        Assert.Equal(110.0 - Math.Sqrt(30.0 * 30.0 - 5.0 * 5.0), stroke.TipY, 9);
        Assert.Equal(Constants.PALETTE[2], stroke.Argb);
    }
}

