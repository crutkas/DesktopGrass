// SwayTests.cs - §6 sway physics.

using System;
using DesktopGrass.Win2D;
using Xunit;

namespace DesktopGrass.Win2D.Tests.SimTests;

public class SwayTests
{
    private static Blade MakeBlade(double phaseOffset = 0.0, double stiffness = 1.0)
        => new()
        {
            BaseX = 100.0,
            Height = 20.0,
            Thickness = 1.5,
            Hue = 3,
            SwayPhaseOffset = phaseOffset,
            Stiffness = stiffness,
            CutHeight = 1.0,
            GustVelocity = 0.0,
            CutAnimStart = -1.0,
            CutInitialHeight = 1.0,
        };

    [Fact]
    public void PureSwayMatchesSinAtTimeZero()
    {
        var b = MakeBlade();
        Sim.UpdateBladeDynamics(ref b, globalTime: 0.0, dt: 1.0 / 60.0);
        Assert.Equal(0.0, b.EffectiveLean, 9);
    }

    [Fact]
    public void PureSwayMatchesSpecFormula()
    {
        var b = MakeBlade(phaseOffset: 1.2, stiffness: 0.8);
        double t = 0.5;
        Sim.UpdateBladeDynamics(ref b, t, dt: 1.0 / 60.0);
        double expected = Math.Sin(1.2 + t * Constants.BASE_SWAY_SPEED)
                          * Constants.BASE_AMPLITUDE * 0.8;
        Assert.Equal(expected, b.EffectiveLean, 9);
    }

    [Fact]
    public void GustDecaysExponentially()
    {
        var b = MakeBlade(phaseOffset: 0.0);
        b.GustVelocity = 10.0;
        double dt = 0.1;
        Sim.UpdateBladeDynamics(ref b, globalTime: 0.0, dt);
        double expectedGust = 10.0 * Math.Exp(-Constants.DECAY_RATE * dt);
        Assert.Equal(expectedGust, b.GustVelocity, 9);
    }

    [Fact]
    public void GustHalfLifeIsApproximately277Ms()
    {
        var b = MakeBlade();
        b.GustVelocity = 100.0;
        Sim.UpdateBladeDynamics(ref b, 0.0, dt: 0.277);
        Assert.InRange(b.GustVelocity, 49.5, 50.5);
    }

    [Fact]
    public void EffectiveLeanIncludesGustContribution()
    {
        var b = MakeBlade();
        b.GustVelocity = 4.0;
        Sim.UpdateBladeDynamics(ref b, 0.0, dt: 0.0);
        Assert.Equal(6.0, b.EffectiveLean, 9);
    }

    [Fact]
    public void LeanBoundedUnderNominalConditions()
    {
        var b = MakeBlade(phaseOffset: 1.0, stiffness: 1.0);
        double maxAbs = 0.0;
        double t = 0.0;
        double dt = 1.0 / 240.0;
        for (int i = 0; i < 1000; i++)
        {
            Sim.UpdateBladeDynamics(ref b, t, dt);
            maxAbs = Math.Max(maxAbs, Math.Abs(b.EffectiveLean));
            t += dt;
        }
        Assert.InRange(maxAbs, Constants.BASE_AMPLITUDE * 0.999, Constants.BASE_AMPLITUDE + 1e-9);
    }

    [Fact]
    public void StiffnessScalesAmplitude()
    {
        var stiff = MakeBlade(phaseOffset: Math.PI / 2.0, stiffness: 1.0);
        var floppy = MakeBlade(phaseOffset: Math.PI / 2.0, stiffness: 0.6);
        Sim.UpdateBladeDynamics(ref stiff, 0.0, 1.0 / 60.0);
        Sim.UpdateBladeDynamics(ref floppy, 0.0, 1.0 / 60.0);
        Assert.Equal(Constants.BASE_AMPLITUDE, stiff.EffectiveLean, 9);
        Assert.Equal(Constants.BASE_AMPLITUDE * 0.6, floppy.EffectiveLean, 9);
    }
}
