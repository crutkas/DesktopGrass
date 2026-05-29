// RegrowthTests.cs — §9 "Regrowth" lifecycle conformance.
//
// Lifecycle: alive (CutHeight=1) -> cut anim (0.2s) -> stump (CutHeight=0,
// RegrowStart scheduled) -> wait RegrowDelay -> regrow (linear over
// RegrowDuration) -> alive again.

using System;
using DesktopGrass.WinUI3;
using Xunit;

namespace DesktopGrass.WinUI3.Tests.SimTests;

public class RegrowthTests
{
    private const double InBandY = 80.0;

    private static Sim MakeSimWithOneBlade(double regrowDelay, double regrowDuration)
    {
        var sim = new Sim(Constants.StripHeight + Constants.Headroom);
        var b = new Blade
        {
            BaseX            = 100.0,
            Height           = 20.0,
            Thickness        = 1.5,
            SwayPhaseOffset  = 0.0,
            Stiffness        = 1.0,
            CutHeight        = 1.0,
            CutInitialHeight = 1.0,
            CutAnimStart     = -1.0,
            RegrowDelay      = regrowDelay,
            RegrowDuration   = regrowDuration,
            RegrowStart      = -1.0,
        };
        sim.TestSetBlades(new[] { b });
        return sim;
    }

    [Fact]
    public void CutCompletionSchedulesRegrowth()
    {
        var sim = MakeSimWithOneBlade(regrowDelay: 1.0, regrowDuration: 0.5);

        sim.TestApplyClick(100.0, InBandY, 0.0);
        for (int i = 0; i < 4; i++) sim.Tick(0.05, ReadOnlySpan<InputEvent>.Empty);

        Assert.Equal(0.0, sim.Blades[0].CutHeight, 9);
        Assert.True(sim.Blades[0].CutAnimStart < 0.0);
        Assert.Equal(1.2, sim.Blades[0].RegrowStart, 9);
    }

    [Fact]
    public void RegrowthIsLinearOverRegrowDuration()
    {
        var sim = MakeSimWithOneBlade(regrowDelay: 0.5, regrowDuration: 0.4);

        sim.TestApplyClick(100.0, InBandY, 0.0);
        for (int i = 0; i < 4; i++) sim.Tick(0.05, ReadOnlySpan<InputEvent>.Empty);
        Assert.Equal(0.70, sim.Blades[0].RegrowStart, 9);

        for (int i = 0; i < 10; i++)
        {
            sim.Tick(0.05, ReadOnlySpan<InputEvent>.Empty);
            Assert.Equal(0.0, sim.Blades[0].CutHeight, 9);
        }

        sim.Tick(0.10, ReadOnlySpan<InputEvent>.Empty);
        Assert.Equal(0.25, sim.Blades[0].CutHeight, 9);
        sim.Tick(0.10, ReadOnlySpan<InputEvent>.Empty);
        Assert.Equal(0.5, sim.Blades[0].CutHeight, 9);
        sim.Tick(0.10, ReadOnlySpan<InputEvent>.Empty);
        Assert.Equal(0.75, sim.Blades[0].CutHeight, 9);
        sim.Tick(0.10, ReadOnlySpan<InputEvent>.Empty);
        Assert.Equal(1.0, sim.Blades[0].CutHeight, 9);
        Assert.True(sim.Blades[0].RegrowStart < 0.0);

        sim.Tick(1.0, ReadOnlySpan<InputEvent>.Empty);
        Assert.Equal(1.0, sim.Blades[0].CutHeight, 9);
    }

    [Fact]
    public void ReClickDuringRegrowthRestartsCutFromCurrentHeight()
    {
        var sim = MakeSimWithOneBlade(regrowDelay: 0.1, regrowDuration: 0.4);

        sim.TestApplyClick(100.0, InBandY, 0.0);
        for (int i = 0; i < 4; i++) sim.Tick(0.05, ReadOnlySpan<InputEvent>.Empty);
        for (int i = 0; i < 2; i++) sim.Tick(0.05, ReadOnlySpan<InputEvent>.Empty);
        for (int i = 0; i < 4; i++) sim.Tick(0.05, ReadOnlySpan<InputEvent>.Empty);
        Assert.Equal(0.5, sim.Blades[0].CutHeight, 9);
        Assert.True(sim.Blades[0].RegrowStart > 0.0);

        sim.TestApplyClick(100.0, InBandY, 0.5);
        Assert.True(sim.Blades[0].CutAnimStart >= 0.0);
        Assert.Equal(0.5, sim.Blades[0].CutInitialHeight, 9);
        Assert.True(sim.Blades[0].RegrowStart < 0.0);

        for (int i = 0; i < 4; i++) sim.Tick(0.05, ReadOnlySpan<InputEvent>.Empty);
        Assert.Equal(0.0, sim.Blades[0].CutHeight, 9);
        Assert.True(sim.Blades[0].CutAnimStart < 0.0);
        Assert.True(sim.Blades[0].RegrowStart > 0.0);
    }

    [Fact]
    public void ClickOnStumpWaitingToRegrowIsNoOp()
    {
        var sim = MakeSimWithOneBlade(regrowDelay: 10.0, regrowDuration: 1.0);
        sim.Blades[0].CutHeight = 0.0;
        sim.Blades[0].CutAnimStart = -1.0;
        sim.Blades[0].RegrowStart = 5.0;

        sim.TestApplyClick(100.0, InBandY, 0.0);

        Assert.Equal(0.0, sim.Blades[0].CutHeight, 9);
        Assert.True(sim.Blades[0].CutAnimStart < 0.0);
        Assert.Equal(5.0, sim.Blades[0].RegrowStart, 9);
    }

    [Fact]
    public void RegrowthJitterIsDeterministicForGivenSeed()
    {
        var a = Sim.GenerateBlades(Constants.CanonicalTestSeed, 1920.0, 1.0);
        var b = Sim.GenerateBlades(Constants.CanonicalTestSeed, 1920.0, 1.0);

        Assert.Equal(a.Length, b.Length);
        for (int i = 0; i < a.Length; i++)
        {
            Assert.Equal(a[i].RegrowDelay, b[i].RegrowDelay, 12);
            Assert.Equal(a[i].RegrowDuration, b[i].RegrowDuration, 12);
        }
    }

    [Fact]
    public void RegrowthJitterFallsWithinConfiguredRange()
    {
        var blades = Sim.GenerateBlades(Constants.CanonicalTestSeed, 1920.0, 1.0);
        Assert.True(blades.Length > 50);
        foreach (ref readonly var b in blades.AsSpan())
        {
            Assert.True(b.RegrowDelay    >= Constants.RegrowDelayMin);
            Assert.True(b.RegrowDelay    <  Constants.RegrowDelayMax);
            Assert.True(b.RegrowDuration >= Constants.RegrowDurationMin);
            Assert.True(b.RegrowDuration <  Constants.RegrowDurationMax);
            Assert.Equal(-1.0, b.RegrowStart);
        }
    }

    [Fact]
    public void DefaultBladeWithoutRegrowJitterStaysCutForever()
    {
        // The "no regrowth" guard means default-constructed Blade (with
        // RegrowDelay = RegrowDuration = 0) stays cut forever after a click.
        // This is exactly how the legacy CutTests fixtures behave.
        var sim = MakeSimWithOneBlade(regrowDelay: 0.0, regrowDuration: 0.0);

        sim.TestApplyClick(100.0, InBandY, 0.0);
        for (int i = 0; i < 60; i++) sim.Tick(0.05, ReadOnlySpan<InputEvent>.Empty);

        Assert.Equal(0.0, sim.Blades[0].CutHeight, 9);
        Assert.True(sim.Blades[0].RegrowStart < 0.0);
    }
}
