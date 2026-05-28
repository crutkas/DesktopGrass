// CutTests.cs — §9 cut state animation conformance.

using System;
using DesktopGrass.WinUI3;
using Xunit;

namespace DesktopGrass.WinUI3.Tests.SimTests;

public class CutTests
{
    private static Sim MakeSim()
    {
        var sim = new Sim(Constants.StripHeight + Constants.Headroom);
        sim.Generate(Constants.CanonicalTestSeed, 1920.0, 1.0);
        return sim;
    }

    private const double InBandY = 80.0;

    [Fact]
    public void ClickOutsideCutBandIgnored()
    {
        var sim = MakeSim();
        sim.TestApplyClick(500.0, 10.0, 0.0);
        foreach (ref readonly var b in sim.Blades.AsSpan())
            Assert.Equal(-1.0, b.CutAnimStart);
        sim.TestApplyClick(500.0, 200.0, 0.0);
        foreach (ref readonly var b in sim.Blades.AsSpan())
            Assert.Equal(-1.0, b.CutAnimStart);
    }

    [Fact]
    public void ClickInsideBandStartsAnimForBladesInRadius()
    {
        var sim = MakeSim();
        sim.TestApplyClick(500.0, InBandY, 0.0);
        bool anyCut = false;
        foreach (ref readonly var b in sim.Blades.AsSpan())
        {
            double dx = Math.Abs(b.BaseX - 500.0);
            if (dx < Constants.CutRadius)
            {
                Assert.True(b.CutAnimStart >= 0.0);
                anyCut = true;
            }
            else
            {
                Assert.Equal(-1.0, b.CutAnimStart);
            }
        }
        Assert.True(anyCut);
    }

    [Fact]
    public void CutHeightLerpsLinearlyToZeroOverDuration()
    {
        // Spec §9: cutHeight goes 1.0 → 0.75 → 0.5 → 0.25 → 0.0 across
        // 4 ticks of dt=0.05s (CutDurationSec = 0.2s), then stays at 0.0.
        var sim = MakeSim();
        sim.TestApplyClick(500.0, InBandY, 0.0);

        int idx = -1;
        for (int i = 0; i < sim.Blades.Length; i++)
        {
            if (Math.Abs(sim.Blades[i].BaseX - 500.0) < Constants.CutRadius
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
        // Spec §12.6: clicking twice on the same blade within the cut
        // animation window does not restart the cut.
        var sim = MakeSim();
        sim.TestApplyClick(500.0, InBandY, 0.0);
        int idx = -1;
        for (int i = 0; i < sim.Blades.Length; i++)
            if (sim.Blades[i].CutAnimStart >= 0.0) { idx = i; break; }
        Assert.True(idx >= 0);
        double originalStart = sim.Blades[idx].CutAnimStart;
        double originalInitial = sim.Blades[idx].CutInitialHeight;

        sim.Tick(0.05, ReadOnlySpan<InputEvent>.Empty);
        sim.TestApplyClick(500.0, InBandY, 0.05);

        Assert.Equal(originalStart, sim.Blades[idx].CutAnimStart);
        Assert.Equal(originalInitial, sim.Blades[idx].CutInitialHeight);
    }

    [Fact]
    public void ClickOnFullyCutBladeIsNoOp()
    {
        var sim = MakeSim();
        sim.TestApplyClick(500.0, InBandY, 0.0);
        // Drive the animation to completion.
        for (int i = 0; i < 5; i++) sim.Tick(0.05, ReadOnlySpan<InputEvent>.Empty);

        int idx = -1;
        for (int i = 0; i < sim.Blades.Length; i++)
        {
            if (Math.Abs(sim.Blades[i].BaseX - 500.0) < Constants.CutRadius
                && sim.Blades[i].CutHeight == 0.0)
            { idx = i; break; }
        }
        Assert.True(idx >= 0);

        sim.TestApplyClick(500.0, InBandY, sim.GlobalTime);
        Assert.Equal(-1.0, sim.Blades[idx].CutAnimStart);
        Assert.Equal(0.0, sim.Blades[idx].CutHeight);
    }

    [Fact]
    public void CutLeavesOutOfRadiusBladesUntouched()
    {
        var sim = MakeSim();
        sim.TestApplyClick(500.0, InBandY, 0.0);
        foreach (ref readonly var b in sim.Blades.AsSpan())
        {
            double dx = Math.Abs(b.BaseX - 500.0);
            if (dx >= Constants.CutRadius)
            {
                Assert.Equal(1.0, b.CutHeight);
                Assert.Equal(-1.0, b.CutAnimStart);
            }
        }
    }
}
