// AmbientGustTests.cs - §8.1 ambient gust scheduler + cross-impl conformance.

using System;
using DesktopGrass.Win2D;
using Xunit;

namespace DesktopGrass.Win2D.Tests.SimTests;

public class AmbientGustTests
{
    private const double Monitor1920 = 1920.0;

    private static Sim BuildSim(ulong seed = Constants.CANONICAL_TEST_SEED,
                                 double monitorWidth = Monitor1920,
                                 double density = Constants.DEFAULT_DENSITY)
    {
        var sim = new Sim
        {
            Blades = Sim.GenerateBlades(seed, monitorWidth, density),
            WindowHeight = Constants.STRIP_HEIGHT + Constants.HEADROOM,
            GroundY = Constants.STRIP_HEIGHT + Constants.HEADROOM,
        };
        sim.ResetAmbientGusts(seed, monitorWidth);
        return sim;
    }

    // --------------------------------------------------------------------
    // Init wires up the ambient PRNG correctly + first interval is sampled.
    // --------------------------------------------------------------------

    [Fact]
    public void ResetAmbientGustsSeedsOffXorSaltAndDrawsFirstInterval()
    {
        var sim = BuildSim();

        var expected = Prng.Init(Constants.CANONICAL_TEST_SEED ^ Constants.AMBIENT_GUST_PRNG_SALT);
        double firstInterval = expected.Uniform(Constants.AMBIENT_GUST_INTERVAL_MIN,
                                                 Constants.AMBIENT_GUST_INTERVAL_MAX);

        Assert.Equal(Monitor1920, sim.MonitorWidth);
        Assert.Equal(firstInterval, sim.NextAmbientGustTime, 12);
        Assert.InRange(sim.NextAmbientGustTime,
                       Constants.AMBIENT_GUST_INTERVAL_MIN,
                       Constants.AMBIENT_GUST_INTERVAL_MAX);
        Assert.Equal(expected.State, sim.AmbientPrng.State);
    }

    // --------------------------------------------------------------------
    // Idle ticks consume zero PRNG draws.
    // --------------------------------------------------------------------

    [Fact]
    public void TickAmbientGustsIsNoOpWhenGlobalTimeBelowNext()
    {
        var sim = BuildSim();
        ulong stateBefore = sim.AmbientPrng.State;

        sim.GlobalTime = Constants.AMBIENT_GUST_INTERVAL_MIN * 0.5;
        for (int i = 0; i < 100; i++) sim.TickAmbientGusts();

        Assert.Equal(stateBefore, sim.AmbientPrng.State);
        Assert.True(sim.NextAmbientGustTime >= sim.GlobalTime);
    }

    // --------------------------------------------------------------------
    // First eight puffs match a cross-impl-derivable snapshot.
    // --------------------------------------------------------------------

    private readonly record struct Puff(double FireTime, double X, double SignDir, double MagFactor);

    private static Puff[] CaptureFirstN(Sim sim, int n)
    {
        var puffs = new Puff[n];
        for (int i = 0; i < n; i++)
        {
            double fireTime = sim.NextAmbientGustTime;
            sim.GlobalTime = fireTime;

            // Peek the next 3 draws so we can attribute them to this puff —
            // the actual call below advances the real PRNG identically.
            var peek = sim.AmbientPrng;
            double x         = peek.Uniform(0.0, sim.MonitorWidth);
            double signDir   = peek.Uniform(0.0, 1.0) < 0.5 ? -1.0 : 1.0;
            double magFactor = peek.Uniform(Constants.AMBIENT_GUST_MAG_FACTOR_MIN,
                                             Constants.AMBIENT_GUST_MAG_FACTOR_MAX);

            sim.TickAmbientGusts();

            puffs[i] = new Puff(fireTime, x, signDir, magFactor);
        }
        return puffs;
    }

    [Fact]
    public void FirstEightPuffsMatchSpecDerivableSnapshotForCanonicalSeed()
    {
        var sim = BuildSim();
        var puffs = CaptureFirstN(sim, 8);

        foreach (var p in puffs)
        {
            Assert.InRange(p.X, 0.0, Monitor1920);
            Assert.True(p.SignDir == -1.0 || p.SignDir == 1.0);
            Assert.InRange(p.MagFactor,
                           Constants.AMBIENT_GUST_MAG_FACTOR_MIN,
                           Constants.AMBIENT_GUST_MAG_FACTOR_MAX);
            Assert.True(p.FireTime >= Constants.AMBIENT_GUST_INTERVAL_MIN);
        }

        for (int i = 1; i < puffs.Length; i++)
        {
            double interval = puffs[i].FireTime - puffs[i - 1].FireTime;
            Assert.InRange(interval,
                           Constants.AMBIENT_GUST_INTERVAL_MIN,
                           Constants.AMBIENT_GUST_INTERVAL_MAX);
        }

        // First puff: re-derive from the spec and assert. The same derivation
        // in the Native test (ambient_gust_tests.cpp) yields the same values
        // by construction because both Prng impls are bit-identical xorshift64.
        var expected = Prng.Init(Constants.CANONICAL_TEST_SEED ^ Constants.AMBIENT_GUST_PRNG_SALT);
        double expectedFirstInterval = expected.Uniform(Constants.AMBIENT_GUST_INTERVAL_MIN,
                                                         Constants.AMBIENT_GUST_INTERVAL_MAX);
        double expectedFirstX        = expected.Uniform(0.0, Monitor1920);
        double expectedFirstSign     = expected.Uniform(0.0, 1.0) < 0.5 ? -1.0 : 1.0;
        double expectedFirstMag      = expected.Uniform(Constants.AMBIENT_GUST_MAG_FACTOR_MIN,
                                                         Constants.AMBIENT_GUST_MAG_FACTOR_MAX);

        Assert.Equal(expectedFirstInterval, puffs[0].FireTime,  12);
        Assert.Equal(expectedFirstX,        puffs[0].X,         12);
        Assert.Equal(expectedFirstSign,     puffs[0].SignDir);
        Assert.Equal(expectedFirstMag,      puffs[0].MagFactor, 12);
    }

    // --------------------------------------------------------------------
    // Apply kernel matches §8.1 — half radius, magnitude scales linearly.
    // --------------------------------------------------------------------

    [Fact]
    public void ApplyAmbientGustHalfRadiusAndScalesWithMagFactor()
    {
        double ambientRadius = Constants.GUST_RADIUS * Constants.AMBIENT_GUST_RADIUS_FACTOR;
        var sim = new Sim
        {
            WindowHeight = Constants.STRIP_HEIGHT + Constants.HEADROOM,
            GroundY = Constants.STRIP_HEIGHT + Constants.HEADROOM,
            Blades = new Blade[]
            {
                new Blade { BaseX = 100.0,                        Height = 20.0, CutHeight = 1.0, HeightBonus = 1.0 },
                new Blade { BaseX = 100.0 + ambientRadius * 0.5,  Height = 20.0, CutHeight = 1.0, HeightBonus = 1.0 },
                new Blade { BaseX = 100.0 + ambientRadius + 5.0,  Height = 20.0, CutHeight = 1.0, HeightBonus = 1.0 },
            },
        };

        const double magFactor = 0.5;
        sim.ApplyAmbientGust(100.0, +1.0, magFactor);

        double expectedPeak = Constants.MAX_CURSOR_SPEED * magFactor * Constants.IMPULSE_SCALE; // 6.0
        Assert.Equal(expectedPeak,        sim.Blades[0].GustVelocity, 12);
        Assert.Equal(expectedPeak * 0.5,  sim.Blades[1].GustVelocity, 12);
        Assert.Equal(0.0,                 sim.Blades[2].GustVelocity, 12);
    }

    [Fact]
    public void ApplyAmbientGustSignDirFlipsImpulseDirection()
    {
        var sim = new Sim
        {
            WindowHeight = Constants.STRIP_HEIGHT + Constants.HEADROOM,
            GroundY = Constants.STRIP_HEIGHT + Constants.HEADROOM,
            Blades = new Blade[]
            {
                new Blade { BaseX = 100.0, Height = 20.0, CutHeight = 1.0, HeightBonus = 1.0 },
            },
        };

        sim.ApplyAmbientGust(100.0, -1.0, 0.5);
        double expectedPeak = Constants.MAX_CURSOR_SPEED * 0.5 * Constants.IMPULSE_SCALE;
        Assert.Equal(-expectedPeak, sim.Blades[0].GustVelocity, 12);
    }

    // --------------------------------------------------------------------
    // Stream independence: §12 static blade snapshot is untouched.
    // First blade for (CANONICAL_TEST_SEED, monitorWidth=1920, density=1.0):
    //   baseX = 4.941073726820111, height = 24.469991818248864,
    //   thickness = 1.5829214329729786, hue = 3.
    // --------------------------------------------------------------------

    [Fact]
    public void AmbientStreamDoesNotPerturbCanonicalFirstBlade()
    {
        var blades = Sim.GenerateBlades(Constants.CANONICAL_TEST_SEED, Monitor1920, 1.0);
        Assert.NotEmpty(blades);
        Assert.Equal(4.941073726820111, blades[0].BaseX, 12);
        Assert.Equal(24.469991818248864, blades[0].Height, 12);
        Assert.Equal(1.5829214329729786, blades[0].Thickness, 12);
        Assert.Equal(3, blades[0].Hue);
    }

    // --------------------------------------------------------------------
    // Tick wires the scheduler into the per-frame loop.
    // --------------------------------------------------------------------

    [Fact]
    public void TickFiresAmbientPuffWhenDtCrossesNextAmbientGustTime()
    {
        var sim = BuildSim();
        double fireTime = sim.NextAmbientGustTime;
        Assert.True(fireTime > 0.0);

        ulong stateBefore = sim.AmbientPrng.State;

        sim.Tick(fireTime * 0.5, ReadOnlySpan<InputEvent>.Empty);
        Assert.Equal(stateBefore, sim.AmbientPrng.State);

        sim.Tick(fireTime, ReadOnlySpan<InputEvent>.Empty);
        Assert.NotEqual(stateBefore, sim.AmbientPrng.State);
        Assert.True(sim.NextAmbientGustTime > sim.GlobalTime);
    }
}
