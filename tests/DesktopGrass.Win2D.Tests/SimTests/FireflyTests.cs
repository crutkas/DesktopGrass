// FireflyTests.cs - §17.7 ambient Firefly tests.

using System;
using System.Collections.Generic;
using System.Linq;
using DesktopGrass.Win2D;
using Xunit;

namespace DesktopGrass.Win2D.Tests.SimTests;

public class FireflyTests
{
    private const double Monitor1920 = 1920.0;

    private static Sim BuildSim(ulong seed = Constants.CANONICAL_TEST_SEED)
    {
        var sim = new Sim
        {
            Blades = Sim.GenerateBlades(seed, Monitor1920, Constants.DEFAULT_DENSITY),
            WindowHeight = Constants.STRIP_HEIGHT + Constants.HEADROOM,
            GroundY = Constants.STRIP_HEIGHT + Constants.HEADROOM,
        };
        sim.ResetAmbientGusts(seed, Monitor1920);
        sim.ResetEntities(seed);
        return sim;
    }

    private static Sim BuildGrassSim(ulong seed = Constants.CANONICAL_TEST_SEED)
    {
        var sim = BuildSim(seed);
        sim.SetScene(Scene.Grass);
        return sim;
    }

    private static int CountKind(Sim sim, EntityKind kind) => sim.Entities.Count(e => e.Kind == kind);

    private static int PrngCount(ref Prng side, int minCount, int maxCount)
    {
        double draw = side.Uniform(minCount, maxCount + 1);
        int count = (int)Math.Floor(draw);
        if (count < minCount) count = minCount;
        if (count > maxCount) count = maxCount;
        return count;
    }

    [Fact]
    public void FireflyConstantsArePinnedToSpecValues()
    {
        Assert.Equal(3, Constants.FIREFLY_COUNT_MIN);
        Assert.Equal(6, Constants.FIREFLY_COUNT_MAX);
        Assert.Equal(4.0, Constants.FIREFLY_DRIFT_SPEED_MIN);
        Assert.Equal(10.0, Constants.FIREFLY_DRIFT_SPEED_MAX);
        Assert.Equal(1.2, Constants.FIREFLY_BODY_RADIUS);
        Assert.Equal(5.0, Constants.FIREFLY_GLOW_RADIUS);
        Assert.Equal(1.4, Constants.FIREFLY_BLINK_PERIOD_MIN);
        Assert.Equal(2.6, Constants.FIREFLY_BLINK_PERIOD_MAX);
        Assert.Equal(0.55, Constants.FIREFLY_BLINK_DUTY);
        Assert.Equal(0.30, Constants.FIREFLY_BLINK_FADE);
        Assert.Equal(0.4, Constants.FIREFLY_DRIFT_FREQ_X);
        Assert.Equal(0.6, Constants.FIREFLY_DRIFT_FREQ_Y);
        Assert.Equal(0.6, Constants.FIREFLY_DRIFT_AMP_X);
        Assert.Equal(8.0, Constants.FIREFLY_DRIFT_AMP_Y);
        Assert.Equal(8.0, Constants.FIREFLY_ALTITUDE_MIN);
        Assert.Equal(55.0, Constants.FIREFLY_ALTITUDE_MAX);
        Assert.Equal(0xFFFFEE88u, Constants.FIREFLY_BODY_COLOR);
        Assert.Equal(0xEEDD66u, Constants.FIREFLY_GLOW_COLOR_RGB);
        Assert.Equal(110, Constants.FIREFLY_GLOW_ALPHA_MAX);
        Assert.Equal(255, Constants.FIREFLY_BODY_ALPHA_MAX);
        Assert.Equal(20, Constants.FIREFLY_NIGHT_START_HOUR);
        Assert.Equal(6, Constants.FIREFLY_NIGHT_END_HOUR);
        Assert.Equal(1, Constants.FIREFLY_FADE_DURATION_HOUR);
        Assert.Equal(0xF13EF1E7777ul, Constants.FIREFLY_PRNG_SALT);
    }

    [Fact]
    public void GrassGenerationProducesFireflyCountInRange()
    {
        for (ulong i = 0; i < 128; i++)
        {
            ulong seed = unchecked(Constants.CANONICAL_TEST_SEED + i * 0x9E3779B97F4A7C15UL);
            var sim = BuildGrassSim(seed);
            Assert.InRange(CountKind(sim, EntityKind.Firefly), Constants.FIREFLY_COUNT_MIN, Constants.FIREFLY_COUNT_MAX);
        }
    }

    [Fact]
    public void FirefliesAreGrassSceneOnly()
    {
        var sim = BuildSim();
        sim.SetScene(Scene.Desert);
        Assert.Equal(0, CountKind(sim, EntityKind.Firefly));
        sim.SetScene(Scene.Winter);
        Assert.Equal(0, CountKind(sim, EntityKind.Firefly));
    }

    [Fact]
    public void GeneratedFirefliesHaveSpeedAltitudeAndBlinkPeriodRanges()
    {
        var sim = BuildGrassSim();
        foreach (var e in sim.Entities.Where(e => e.Kind == EntityKind.Firefly))
        {
            Assert.InRange(e.BaseSpeed, Constants.FIREFLY_DRIFT_SPEED_MIN, Constants.FIREFLY_DRIFT_SPEED_MAX);
            Assert.InRange(e.AltitudeAnchor, Constants.FIREFLY_ALTITUDE_MIN, Constants.FIREFLY_ALTITUDE_MAX);
            Assert.InRange(e.BlinkPeriod, Constants.FIREFLY_BLINK_PERIOD_MIN, Constants.FIREFLY_BLINK_PERIOD_MAX);
        }
    }

    [Fact]
    public void FireflyPrngDrawOrderMatchesSideStream()
    {
        var side = Prng.Init(Constants.CANONICAL_TEST_SEED ^ Constants.FIREFLY_PRNG_SALT);
        var sim = BuildGrassSim();

        int expectedCount = PrngCount(ref side, Constants.FIREFLY_COUNT_MIN, Constants.FIREFLY_COUNT_MAX);
        Assert.Equal(expectedCount, CountKind(sim, EntityKind.Firefly));

        int seen = 0;
        foreach (var e in sim.Entities.Where(e => e.Kind == EntityKind.Firefly))
        {
            double xFrac = side.Uniform(0.0, 1.0);
            double yFrac = side.Uniform(0.0, 1.0);
            ulong vxSign = side.NextU64() & 1UL;
            double expectedDir = vxSign != 0UL ? 1.0 : -1.0;
            double expectedSpeed = side.Uniform(Constants.FIREFLY_DRIFT_SPEED_MIN, Constants.FIREFLY_DRIFT_SPEED_MAX);
            double expectedBlinkPeriod = side.Uniform(Constants.FIREFLY_BLINK_PERIOD_MIN, Constants.FIREFLY_BLINK_PERIOD_MAX);
            double expectedBlinkPhase = side.Uniform(0.0, 1.0);
            double expectedPhaseY = side.Uniform(0.0, 2.0 * Math.PI);
            double expectedPhaseX = side.Uniform(0.0, 2.0 * Math.PI);
            double expectedAltitude = Constants.FIREFLY_ALTITUDE_MIN
                + yFrac * (Constants.FIREFLY_ALTITUDE_MAX - Constants.FIREFLY_ALTITUDE_MIN);
            double expectedVx = expectedDir * expectedSpeed
                * (1.0 + Constants.FIREFLY_DRIFT_AMP_X * Math.Sin(expectedPhaseX));

            Assert.Equal(xFrac * Monitor1920, e.X, 9);
            Assert.Equal(expectedAltitude, e.AltitudeAnchor, 9);
            Assert.Equal(expectedSpeed, e.BaseSpeed, 9);
            Assert.Equal(expectedBlinkPeriod, e.BlinkPeriod, 9);
            Assert.Equal(expectedBlinkPhase, e.BlinkPhase, 9);
            Assert.Equal(expectedVx, e.Vx, 9);
            Assert.Equal(expectedPhaseY, e.PhaseY, 9);
            Assert.Equal(expectedPhaseX, e.PhaseX, 9);
            seen++;
        }
        Assert.Equal(expectedCount, seen);
    }

    [Fact]
    public void FireflyEdgeWrapPreservesAltitudeAnchor()
    {
        var sim = BuildGrassSim();
        int idx = sim.Entities.FindIndex(e => e.Kind == EntityKind.Firefly);
        Assert.True(idx >= 0);
        double margin = Constants.FIREFLY_GLOW_RADIUS;
        var e = sim.Entities[idx];
        e.X = Monitor1920 + margin + 1.0;
        e.Vx = Math.Abs(e.Vx);
        double altitude = e.AltitudeAnchor;
        sim.Entities[idx] = e;
        sim.CurrentScene = Scene.Desert;

        sim.TickEntities(0.016);

        Assert.Equal(-margin, sim.Entities[idx].X, 9);
        Assert.Equal(altitude, sim.Entities[idx].AltitudeAnchor, 9);
    }

    [Fact]
    public void FirefliesDoNotInteractWithCutsOrPets()
    {
        var sim = BuildSim();
        sim.Entities.Clear();
        var firefly = new Entity
        {
            Kind = EntityKind.Firefly,
            X = 500.0,
            Y = sim.WindowHeight - Constants.STRIP_HEIGHT - 5.0,
            Vx = Constants.FIREFLY_DRIFT_SPEED_MIN,
            BaseSpeed = Constants.FIREFLY_DRIFT_SPEED_MIN,
            AltitudeAnchor = Constants.FIREFLY_ALTITUDE_MIN,
            BlinkPeriod = Constants.FIREFLY_BLINK_PERIOD_MIN,
            Lifetime = -1.0,
        };
        sim.Entities.Add(firefly);
        sim.Entities.Add(new Entity
        {
            Kind = EntityKind.Sheep,
            X = firefly.X,
            Y = sim.WindowHeight - Constants.SHEEP_BODY_HEIGHT - Constants.SHEEP_LEG_LENGTH,
            Vx = Constants.SHEEP_WALK_SPEED_MIN,
            State = Constants.SHEEP_STATE_WALKING,
            StateTimer = 10.0,
        });

        sim.ApplyClick(firefly.X, firefly.Y, 0.0);

        Assert.Equal(EntityKind.Firefly, sim.Entities[0].Kind);
        Assert.Equal(Constants.FIREFLY_DRIFT_SPEED_MIN, sim.Entities[0].BaseSpeed, 9);
        Assert.Equal(Constants.SHEEP_STATE_WALKING, sim.Entities[1].State);
        Assert.All(sim.Blades, b => Assert.True(b.CutAnimStart < 0.0));
    }

    [Fact]
    public void FireflyBlinkBrightnessHasOnAndOffPhases()
    {
        const double period = 2.0;
        Assert.Equal(1.0, Constants.FireflyBlinkBrightness(period * 0.25, period, 0.0), 9);
        Assert.Equal(0.0, Constants.FireflyBlinkBrightness(period * 0.80, period, 0.0), 9);
    }

    [Fact]
    public void FireflyPhasesDecorrelateVisibleBrightness()
    {
        const double period = 2.0;
        double[] phases = { 0.0, 0.0375, 0.075, 0.1125, 0.25, 0.80 };
        var distinct = new List<double>();
        foreach (double phase in phases)
        {
            double brightness = Constants.FireflyBlinkBrightness(0.0, period, phase);
            if (!distinct.Any(existing => Math.Abs(existing - brightness) < 1e-6))
            {
                distinct.Add(brightness);
            }
        }
        Assert.True(distinct.Count >= 4);
    }

    [Fact]
    public void FireflyFadeIsNightOnlyWithDuskAndDawnRamps()
    {
        Assert.Equal(1.0, Constants.FireflyFade(0.0), 9);
        Assert.Equal(0.0, Constants.FireflyFade(12.0), 9);
        Assert.Equal(0.5, Constants.FireflyFade(19.5), 9);
        Assert.Equal(0.5, Constants.FireflyFade(6.5), 9);
    }
}
