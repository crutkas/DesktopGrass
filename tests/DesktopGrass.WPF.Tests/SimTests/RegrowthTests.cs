// RegrowthTests.cs - §9 "Regrowth" lifecycle.
//
// Lifecycle: alive (CutHeight=1) -> cut anim (0.2s) -> stump (CutHeight=0,
// RegrowStart scheduled) -> wait RegrowDelay -> regrow (linear over
// RegrowDuration) -> alive again.

using System;
using DesktopGrass.WPF;
using Xunit;

namespace DesktopGrass.WPF.Tests.SimTests;

public class RegrowthTests
{
    private static Sim MakeSimWithOneBlade(double regrowDelay, double regrowDuration)
    {
        // A controlled test blade: opt in to regrowth with known timing.
        Blade b = default;
        b.BaseX = 100.0;
        b.Height = 20.0;
        b.Thickness = 1.5;
        b.SwayPhaseOffset = 0.0;
        b.Stiffness = 1.0;
        b.CutHeight = 1.0;
        b.CutInitialHeight = 1.0;
        b.CutAnimStart = -1.0;
        b.RegrowDelay = regrowDelay;
        b.RegrowDuration = regrowDuration;
        b.RegrowStart = -1.0;

        return new Sim
        {
            Blades = new[] { b },
            GroundY = 110.0,
            WindowHeight = 110.0,
        };
    }

    [Fact]
    public void CutCompletionSchedulesRegrowth()
    {
        var sim = MakeSimWithOneBlade(regrowDelay: 1.0, regrowDuration: 0.5);

        sim.ApplyClick(100.0, sim.GroundY - 40.0, 0.0);
        for (int i = 0; i < 4; i++) sim.Tick(0.05, ReadOnlySpan<InputEvent>.Empty);

        Assert.Equal(0.0, sim.Blades[0].CutHeight, 9);
        Assert.True(sim.Blades[0].CutAnimStart < 0.0);
        Assert.Equal(1.2, sim.Blades[0].RegrowStart, 9);
    }

    [Fact]
    public void RegrowthIsLinearOverRegrowDuration()
    {
        var sim = MakeSimWithOneBlade(regrowDelay: 0.5, regrowDuration: 0.4);

        sim.ApplyClick(100.0, sim.GroundY - 40.0, 0.0);
        for (int i = 0; i < 4; i++) sim.Tick(0.05, ReadOnlySpan<InputEvent>.Empty);
        Assert.Equal(0.70, sim.Blades[0].RegrowStart, 9);

        // Tick through the delay window — blade stays cut.
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

        // Idle after regrowth.
        sim.Tick(1.0, ReadOnlySpan<InputEvent>.Empty);
        Assert.Equal(1.0, sim.Blades[0].CutHeight, 9);
    }

    [Fact]
    public void ReClickDuringRegrowthRestartsCutFromCurrentHeight()
    {
        var sim = MakeSimWithOneBlade(regrowDelay: 0.1, regrowDuration: 0.4);

        sim.ApplyClick(100.0, sim.GroundY - 40.0, 0.0);
        // 4 ticks of 50 ms = cut done (globalTime=0.20, regrowStart=0.30).
        for (int i = 0; i < 4; i++) sim.Tick(0.05, ReadOnlySpan<InputEvent>.Empty);
        // 2 ticks of 50 ms -> globalTime=0.30 (regrowth begins).
        for (int i = 0; i < 2; i++) sim.Tick(0.05, ReadOnlySpan<InputEvent>.Empty);
        // 4 ticks of 50 ms -> 200 ms into the 0.4s regrowth -> CutHeight=0.5.
        for (int i = 0; i < 4; i++) sim.Tick(0.05, ReadOnlySpan<InputEvent>.Empty);
        Assert.Equal(0.5, sim.Blades[0].CutHeight, 9);
        Assert.True(sim.Blades[0].RegrowStart > 0.0);

        sim.ApplyClick(100.0, sim.GroundY - 40.0, sim.GlobalTime);
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

        sim.ApplyClick(100.0, sim.GroundY - 40.0, 0.0);

        Assert.Equal(0.0, sim.Blades[0].CutHeight, 9);
        Assert.True(sim.Blades[0].CutAnimStart < 0.0);
        Assert.Equal(5.0, sim.Blades[0].RegrowStart, 9);
    }

    [Fact]
    public void RegrowthJitterIsDeterministicForGivenSeed()
    {
        var a = Sim.GenerateBlades(Constants.CANONICAL_TEST_SEED, 1920.0, 1.0);
        var b = Sim.GenerateBlades(Constants.CANONICAL_TEST_SEED, 1920.0, 1.0);

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
        var blades = Sim.GenerateBlades(Constants.CANONICAL_TEST_SEED, 1920.0, 1.0);
        Assert.True(blades.Length > 50);
        foreach (ref readonly var b in blades.AsSpan())
        {
            Assert.True(b.RegrowDelay    >= Constants.REGROW_DELAY_MIN);
            Assert.True(b.RegrowDelay    <  Constants.REGROW_DELAY_MAX);
            Assert.True(b.RegrowDuration >= Constants.REGROW_DURATION_MIN);
            Assert.True(b.RegrowDuration <  Constants.REGROW_DURATION_MAX);
            Assert.Equal(-1.0, b.RegrowStart);
        }
    }

    [Fact]
    public void DefaultBladeWithoutRegrowJitterStaysCutForever()
    {
        // A default-constructed Blade has RegrowDelay = RegrowDuration = 0,
        // which is the guard sentinel for "no regrowth". Production blades
        // always have positive values; this test pins the legacy contract for
        // test fixtures (and is exactly why the existing CutTests still pass).
        var sim = MakeSimWithOneBlade(regrowDelay: 0.0, regrowDuration: 0.0);

        sim.ApplyClick(100.0, sim.GroundY - 40.0, 0.0);
        for (int i = 0; i < 60; i++) sim.Tick(0.05, ReadOnlySpan<InputEvent>.Empty);

        Assert.Equal(0.0, sim.Blades[0].CutHeight, 9);
        Assert.True(sim.Blades[0].RegrowStart < 0.0);
    }
}

