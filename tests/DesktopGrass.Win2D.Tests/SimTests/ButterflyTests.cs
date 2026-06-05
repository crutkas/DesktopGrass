// ButterflyTests.cs - §17.6 ambient Butterfly tests.

using System;
using System.Linq;
using DesktopGrass.Win2D;
using Xunit;

namespace DesktopGrass.Win2D.Tests.SimTests;

public class ButterflyTests
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
    public void ButterflyConstantsArePinnedToSpecValues()
    {
        Assert.Equal(2, Constants.BUTTERFLY_COUNT_MIN);
        Assert.Equal(4, Constants.BUTTERFLY_COUNT_MAX);
        Assert.Equal(18.0, Constants.BUTTERFLY_SPEED_MIN);
        Assert.Equal(32.0, Constants.BUTTERFLY_SPEED_MAX);
        Assert.Equal(2.4, Constants.BUTTERFLY_BODY_LENGTH);
        Assert.Equal(3.5, Constants.BUTTERFLY_WING_RADIUS);
        Assert.Equal(2.2, Constants.BUTTERFLY_WING_OFFSET);
        Assert.Equal(16.0, Constants.BUTTERFLY_FLUTTER_FREQ);
        Assert.Equal(0.20, Constants.BUTTERFLY_FLUTTER_MIN_SCALE);
        Assert.Equal(0.8, Constants.BUTTERFLY_MEANDER_FREQ_Y);
        Assert.Equal(16.0, Constants.BUTTERFLY_MEANDER_AMP_Y);
        Assert.Equal(0.5, Constants.BUTTERFLY_MEANDER_FREQ_X);
        Assert.Equal(0.4, Constants.BUTTERFLY_MEANDER_AMP_X);
        Assert.Equal(18.0, Constants.BUTTERFLY_ALTITUDE_MIN);
        Assert.Equal(70.0, Constants.BUTTERFLY_ALTITUDE_MAX);
        Assert.Equal(0xFF2A2018u, Constants.BUTTERFLY_BODY_COLOR);
        Assert.Equal(5, Constants.BUTTERFLY_COLOR_COUNT);
        Assert.Equal(0xB07DEF1E0001ul, Constants.BUTTERFLY_PRNG_SALT);
    }

    [Fact]
    public void GrassGenerationProducesButterflyCountInRange()
    {
        for (ulong i = 0; i < 128; i++)
        {
            ulong seed = unchecked(Constants.CANONICAL_TEST_SEED + i * 0x9E3779B97F4A7C15UL);
            var sim = BuildGrassSim(seed);
            Assert.InRange(CountKind(sim, EntityKind.Butterfly), Constants.BUTTERFLY_COUNT_MIN, Constants.BUTTERFLY_COUNT_MAX);
        }
    }

    [Fact]
    public void ButterfliesAreGrassSceneOnly()
    {
        var sim = BuildSim();
        sim.SetScene(Scene.Desert);
        Assert.Equal(0, CountKind(sim, EntityKind.Butterfly));
        sim.SetScene(Scene.Winter);
        Assert.Equal(0, CountKind(sim, EntityKind.Butterfly));
    }

    [Fact]
    public void GeneratedButterfliesHaveSpeedAltitudeAndColorRanges()
    {
        var sim = BuildGrassSim();
        foreach (var e in sim.Entities.Where(e => e.Kind == EntityKind.Butterfly))
        {
            Assert.InRange(e.BaseSpeed, Constants.BUTTERFLY_SPEED_MIN, Constants.BUTTERFLY_SPEED_MAX);
            Assert.InRange(e.AltitudeAnchor, Constants.BUTTERFLY_ALTITUDE_MIN, Constants.BUTTERFLY_ALTITUDE_MAX);
            Assert.True(e.ColorVariant < Constants.BUTTERFLY_COLOR_COUNT);
        }
    }

    [Fact]
    public void ButterflyPrngDrawOrderMatchesSideStream()
    {
        var side = Prng.Init(Constants.CANONICAL_TEST_SEED ^ Constants.BUTTERFLY_PRNG_SALT);
        var sim = BuildGrassSim();

        int expectedCount = PrngCount(ref side, Constants.BUTTERFLY_COUNT_MIN, Constants.BUTTERFLY_COUNT_MAX);
        Assert.Equal(expectedCount, CountKind(sim, EntityKind.Butterfly));

        int seen = 0;
        foreach (var e in sim.Entities.Where(e => e.Kind == EntityKind.Butterfly))
        {
            double xFrac = side.Uniform(0.0, 1.0);
            double yFrac = side.Uniform(0.0, 1.0);
            ulong vxSign = side.NextU64() & 1UL;
            double expectedDir = vxSign != 0UL ? 1.0 : -1.0;
            double expectedSpeed = side.Uniform(Constants.BUTTERFLY_SPEED_MIN, Constants.BUTTERFLY_SPEED_MAX);
            byte expectedColor = (byte)side.Index((uint)Constants.BUTTERFLY_COLOR_COUNT);
            double expectedPhaseY = side.Uniform(0.0, 2.0 * Math.PI);
            double expectedPhaseX = side.Uniform(0.0, 2.0 * Math.PI);
            double expectedAltitude = Constants.BUTTERFLY_ALTITUDE_MIN
                + yFrac * (Constants.BUTTERFLY_ALTITUDE_MAX - Constants.BUTTERFLY_ALTITUDE_MIN);
            double expectedVx = expectedDir * expectedSpeed
                * (1.0 + Constants.BUTTERFLY_MEANDER_AMP_X * Math.Sin(expectedPhaseX));

            Assert.Equal(xFrac * Monitor1920, e.X, 9);
            Assert.Equal(expectedAltitude, e.AltitudeAnchor, 9);
            Assert.Equal(expectedSpeed, e.BaseSpeed, 9);
            Assert.Equal(expectedVx, e.Vx, 9);
            Assert.Equal(expectedColor, e.ColorVariant);
            Assert.Equal(expectedPhaseY, e.PhaseY, 9);
            Assert.Equal(expectedPhaseX, e.PhaseX, 9);
            seen++;
        }
        Assert.Equal(expectedCount, seen);
    }

    [Fact]
    public void ButterflyEdgeWrapPreservesAltitudeAnchor()
    {
        var sim = BuildGrassSim();
        int idx = sim.Entities.FindIndex(e => e.Kind == EntityKind.Butterfly);
        Assert.True(idx >= 0);
        double margin = Constants.BUTTERFLY_WING_OFFSET + Constants.BUTTERFLY_WING_RADIUS;
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
    public void ButterfliesDoNotInteractWithCutsOrPets()
    {
        var sim = BuildSim();
        sim.Entities.Clear();
        var butterfly = new Entity
        {
            Kind = EntityKind.Butterfly,
            X = 500.0,
            Y = sim.WindowHeight - Constants.STRIP_HEIGHT - 5.0,
            Vx = Constants.BUTTERFLY_SPEED_MIN,
            BaseSpeed = Constants.BUTTERFLY_SPEED_MIN,
            AltitudeAnchor = Constants.BUTTERFLY_ALTITUDE_MIN,
            Lifetime = -1.0,
        };
        sim.Entities.Add(butterfly);
        sim.Entities.Add(new Entity
        {
            Kind = EntityKind.Sheep,
            X = butterfly.X,
            Y = sim.WindowHeight - Constants.SHEEP_BODY_HEIGHT - Constants.SHEEP_LEG_LENGTH,
            Vx = Constants.SHEEP_WALK_SPEED_MIN,
            State = Constants.SHEEP_STATE_WALKING,
            StateTimer = 10.0,
        });

        sim.ApplyClick(butterfly.X, butterfly.Y, 0.0);

        Assert.Equal(EntityKind.Butterfly, sim.Entities[0].Kind);
        Assert.Equal(Constants.BUTTERFLY_SPEED_MIN, sim.Entities[0].BaseSpeed, 9);
        Assert.Equal(Constants.SHEEP_STATE_WALKING, sim.Entities[1].State);
        Assert.All(sim.Blades, b => Assert.True(b.CutAnimStart < 0.0));
    }

    [Fact]
    public void ButterflyWingScaleStaysWithinFlutterBounds()
    {
        for (int i = 0; i < 200; i++)
        {
            double scale = Constants.ButterflyWingScale(i * 0.05, 1.3);
            Assert.InRange(scale, Constants.BUTTERFLY_FLUTTER_MIN_SCALE, 1.0);
        }
    }

}
