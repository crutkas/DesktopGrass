// CutTests.cs - §9 cut state animation.

using System;
using DesktopGrass.Win2D;
using Xunit;

namespace DesktopGrass.Win2D.Tests.SimTests;

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

        // Production blades settle at their per-blade stubble floor, so the
        // cut animation lerps 1.0 -> floor rather than 1.0 -> 0.
        double f = sim.Blades[idx].CutFloor;
        Assert.Equal(1.0, heights[0], 9);
        Assert.Equal(f + (1.0 - f) * 0.75, heights[1], 9);
        Assert.Equal(f + (1.0 - f) * 0.50, heights[2], 9);
        Assert.Equal(f + (1.0 - f) * 0.25, heights[3], 9);
        Assert.Equal(f, heights[4], 9);
        Assert.Equal(f, heights[5], 9);
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
                && sim.Blades[i].CutAnimStart < 0.0
                && sim.Blades[i].CutHeight == sim.Blades[i].CutFloor)
            { idx = i; break; }
        }
        Assert.True(idx >= 0);

        double floor = sim.Blades[idx].CutFloor;
        sim.ApplyClick(500.0, 80.0, sim.GlobalTime);
        Assert.Equal(-1.0, sim.Blades[idx].CutAnimStart);
        Assert.Equal(floor, sim.Blades[idx].CutHeight);
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
        var stroke = Sim.ComputeBladeStroke(b, groundY: 110.0, Scene.Grass);
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
            HeightBonus = 1.0,
            EffectiveLean = 5.0,
        };
        var stroke = Sim.ComputeBladeStroke(b, groundY: 110.0, Scene.Grass);
        Assert.Equal(100.0, stroke.BaseX);
        Assert.Equal(110.0, stroke.BaseY);
        Assert.Equal(105.0, stroke.TipX, 9);
        Assert.Equal(110.0 - Math.Sqrt(30.0 * 30.0 - 5.0 * 5.0), stroke.TipY, 9);
        Assert.Equal(Constants.PALETTE[2], stroke.Argb);
    }

    [Fact]
    public void GeneratedBladesGetCutFloorWithinSpecRange()
    {
        var blades = Sim.GenerateBlades(Constants.CANONICAL_TEST_SEED, 1920.0, 1.0);
        Assert.True(blades.Length > 50);

        foreach (ref readonly var b in blades.AsSpan())
        {
            Assert.InRange(b.CutFloor, Constants.CUT_FLOOR_MIN, Constants.CUT_FLOOR_MAX);
            Assert.True(b.CutFloor >= Constants.CUT_STUMP_THRESHOLD);
        }

        bool varies = false;
        for (int i = 1; i < blades.Length; i++)
        {
            if (blades[i].CutFloor != blades[0].CutFloor) { varies = true; break; }
        }
        Assert.True(varies);
    }

    [Fact]
    public void CutSettlesAtStubbleFloorNotZero()
    {
        var b = new Blade
        {
            Height = 20.0,
            CutHeight = 1.0,
            CutInitialHeight = 1.0,
            CutFloor = 0.12,
            CutAnimStart = 0.0,
        };
        Sim.AdvanceCut(ref b, Constants.CUT_DURATION_SEC + 0.01);
        Assert.Equal(0.12, b.CutHeight, 9);
        Assert.Equal(-1.0, b.CutAnimStart);
    }

    [Fact]
    public void CutDownLerpsTowardFloor()
    {
        var b = new Blade
        {
            Height = 20.0,
            CutHeight = 1.0,
            CutInitialHeight = 1.0,
            CutFloor = 0.10,
            CutAnimStart = 0.0,
        };
        Sim.AdvanceCut(ref b, Constants.CUT_DURATION_SEC * 0.5);
        Assert.Equal(0.10 + 0.90 * 0.5, b.CutHeight, 9);
    }

    [Fact]
    public void RegrowthGrowsBackFromFloor()
    {
        var b = new Blade
        {
            Height = 20.0,
            CutFloor = 0.10,
            CutHeight = 0.10,
            CutAnimStart = -1.0,
            RegrowDuration = 0.4,
            RegrowStart = 0.0,
        };
        Sim.AdvanceCut(ref b, 0.2);
        Assert.Equal(0.10 + 0.90 * 0.5, b.CutHeight, 9);

        Sim.AdvanceCut(ref b, 0.4);
        Assert.Equal(1.0, b.CutHeight, 9);
    }

    [Fact]
    public void ZeroFloorBladeStillCollapsesFully()
    {
        var b = new Blade
        {
            Height = 20.0,
            CutHeight = 1.0,
            CutInitialHeight = 1.0,
            CutFloor = 0.0,
            CutAnimStart = 0.0,
        };
        Sim.AdvanceCut(ref b, Constants.CUT_DURATION_SEC + 0.01);
        Assert.Equal(0.0, b.CutHeight, 9);
    }
}
