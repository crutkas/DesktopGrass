// CritterTests.cs - §13.3 / §16 Critter subsystem (orthogonal to Scene).
//
// Mirrors tests/DesktopGrass.Native.Tests/src/critter_tests.cpp.

using System.Linq;
using DesktopGrass.Win2D;
using Xunit;

namespace DesktopGrass.Win2D.Tests.SimTests;

public class CritterTests
{
    private const double Monitor1920 = 1920.0;

    private static Sim BuildSim(ulong seed = Constants.CANONICAL_TEST_SEED,
                                double monitorWidth = Monitor1920,
                                double density = 1.0)
    {
        var sim = new Sim
        {
            Blades = Sim.GenerateBlades(seed, monitorWidth, density),
            WindowHeight = Constants.STRIP_HEIGHT + Constants.HEADROOM,
            GroundY = Constants.STRIP_HEIGHT + Constants.HEADROOM,
        };
        sim.ResetAmbientGusts(seed, monitorWidth);
        sim.ResetEntities(seed);
        return sim;
    }

    private static int CountSheep(Sim sim) =>
        sim.Entities.Count(e => e.Kind == EntityKind.Sheep);

    private static int IndexOfFirstSheep(Sim sim)
    {
        for (int i = 0; i < sim.Entities.Count; i++)
            if (sim.Entities[i].Kind == EntityKind.Sheep) return i;
        return -1;
    }

    [Fact]
    public void CritterKindHasSpecLockedDiscriminants()
    {
        Assert.Equal(0, (int)CritterKind.None);
        Assert.Equal(1, (int)CritterKind.Sheep);
        Assert.Equal(3, (int)EntityKind.Sheep);
        Assert.Equal(CritterKind.None, Constants.CRITTER_DEFAULT);
    }

    [Fact]
    public void SheepConstantsArePinnedToSpecValues()
    {
        Assert.Equal(2, Constants.SHEEP_COUNT_MIN);
        Assert.Equal(3, Constants.SHEEP_COUNT_MAX);
        Assert.Equal(14.0, Constants.SHEEP_WALK_SPEED_MIN);
        Assert.Equal(26.0, Constants.SHEEP_WALK_SPEED_MAX);
        Assert.Equal(12.0, Constants.SHEEP_BODY_RADIUS);
        Assert.Equal(5.0,  Constants.SHEEP_HEAD_RADIUS);
        Assert.Equal(5.5,  Constants.SHEEP_LEG_LENGTH);

        Assert.Equal((byte)0, Constants.SHEEP_STATE_WALKING);
        Assert.Equal((byte)1, Constants.SHEEP_STATE_GRAZING);
        Assert.Equal((byte)2, Constants.SHEEP_STATE_IDLE);
        Assert.Equal((byte)3, Constants.SHEEP_STATE_SLEEPING);
        Assert.Equal((byte)4, Constants.SHEEP_STATE_HOPPING);

        Assert.Equal(0.55, Constants.SHEEP_HOP_DURATION);
        Assert.Equal(11.0, Constants.SHEEP_HOP_HEIGHT);
        Assert.Equal(64.0, Constants.SHEEP_STARTLE_RADIUS);
        Assert.Equal(1.6,  Constants.SHEEP_STARTLE_BOOST);

        Assert.Equal(0.60, Constants.SHEEP_GRAZE_PROBABILITY);
        Assert.Equal(0.25, Constants.SHEEP_IDLE_PROBABILITY);
        Assert.Equal(0.10, Constants.SHEEP_SLEEP_PROB_MORNING);
        Assert.Equal(0.30, Constants.SHEEP_SLEEP_PROB_DEFAULT);
        Assert.Equal(0.70, Constants.SHEEP_SLEEP_PROB_NIGHT);
        Assert.Equal(6, Constants.SHEEP_MORNING_START_HOUR);
        Assert.Equal(10, Constants.SHEEP_MORNING_END_HOUR);
        Assert.Equal(22, Constants.SHEEP_NIGHT_START_HOUR);
        Assert.Equal(6, Constants.SHEEP_NIGHT_END_HOUR);
        Assert.Equal(Constants.SHEEP_SLEEP_PROB_DEFAULT, Constants.SHEEP_SLEEP_FROM_IDLE_PROB);
    }

    [Fact]
    public void SimDefaultsCritterToNone()
    {
        var sim = new Sim();
        Assert.Equal(CritterKind.None, sim.CurrentCritter);
        Assert.Empty(sim.Entities);
    }

    [Fact]
    public void SetCritterSheepProducesDeterministicFlock()
    {
        var sim = BuildSim();
        sim.SetCritter(CritterKind.Sheep);

        Assert.Equal(CritterKind.Sheep, sim.CurrentCritter);
        int k = CountSheep(sim);
        Assert.InRange(k, Constants.SHEEP_COUNT_MIN, Constants.SHEEP_COUNT_MAX);

        double groundY = sim.WindowHeight;
        foreach (var e in sim.Entities)
        {
            if (e.Kind != EntityKind.Sheep) continue;
            Assert.Equal(Constants.SHEEP_STATE_WALKING, e.State);
            Assert.InRange(e.StateTimer,
                Constants.SHEEP_WALK_DURATION_MIN,
                Constants.SHEEP_WALK_DURATION_MAX);
            double speed = System.Math.Abs(e.Vx);
            Assert.InRange(speed,
                Constants.SHEEP_WALK_SPEED_MIN,
                Constants.SHEEP_WALK_SPEED_MAX);
            double margin = e.Size + 8.0;
            Assert.InRange(e.X, margin, sim.MonitorWidth - margin);
            Assert.Equal(groundY - Constants.SHEEP_BODY_HEIGHT - Constants.SHEEP_LEG_LENGTH,
                         e.Y, 9);
            Assert.True(e.Lifetime < 0.0); // sheep don't expire
        }
    }

    [Fact]
    public void SheepPrngDrawOrderMatchesSideStream()
    {
        // Independent side stream walking the locked sequence:
        //   count
        //   per-sheep: x, speed, dir-coin, seed, stateTimer
        var side = Prng.Init(Constants.CANONICAL_TEST_SEED ^ Constants.CRITTER_PRNG_SALT);

        var sim = BuildSim();
        sim.SetCritter(CritterKind.Sheep);

        double countDraw = side.Uniform(Constants.SHEEP_COUNT_MIN, Constants.SHEEP_COUNT_MAX + 1);
        int expectedCount = (int)System.Math.Floor(countDraw);
        if (expectedCount < Constants.SHEEP_COUNT_MIN) expectedCount = Constants.SHEEP_COUNT_MIN;
        if (expectedCount > Constants.SHEEP_COUNT_MAX) expectedCount = Constants.SHEEP_COUNT_MAX;
        Assert.Equal(expectedCount, CountSheep(sim));

        int seen = 0;
        foreach (var e in sim.Entities)
        {
            if (e.Kind != EntityKind.Sheep) continue;
            double margin = Constants.SHEEP_BODY_RADIUS + 8.0;
            double expectedX = side.Uniform(margin, Monitor1920 - margin);
            double expectedSpeed = side.Uniform(
                Constants.SHEEP_WALK_SPEED_MIN, Constants.SHEEP_WALK_SPEED_MAX);
            double dirCoin = side.Uniform(0.0, 1.0);
            double expectedDir = dirCoin < 0.5 ? -1.0 : 1.0;
            uint expectedSeed = side.NextU32();
            double expectedTimer = side.Uniform(
                Constants.SHEEP_WALK_DURATION_MIN, Constants.SHEEP_WALK_DURATION_MAX);

            Assert.Equal(expectedX, e.X, 9);
            Assert.Equal(expectedSpeed * expectedDir, e.Vx, 9);
            Assert.Equal(expectedSeed, e.Seed);
            Assert.Equal(expectedTimer, e.StateTimer, 9);
            seen++;
        }
        Assert.Equal(expectedCount, seen);
    }

    [Fact]
    public void SetCritterNoneRemovesSheepPreservesSceneEntities()
    {
        var sim = BuildSim();
        sim.SetScene(Scene.Desert);
        int tumbleBefore = sim.Entities.Count(e => e.Kind == EntityKind.Tumbleweed);

        sim.SetCritter(CritterKind.Sheep);
        Assert.True(CountSheep(sim) >= Constants.SHEEP_COUNT_MIN);
        int tumbleAfterSheep = sim.Entities.Count(e => e.Kind == EntityKind.Tumbleweed);
        Assert.Equal(tumbleBefore, tumbleAfterSheep);

        sim.SetCritter(CritterKind.None);
        Assert.Equal(0, CountSheep(sim));
        int tumbleAfterNone = sim.Entities.Count(e => e.Kind == EntityKind.Tumbleweed);
        Assert.Equal(tumbleBefore, tumbleAfterNone);
    }

    [Fact]
    public void SetScenePreservesActiveCritter()
    {
        var sim = BuildSim();
        sim.SetCritter(CritterKind.Sheep);
        int sheepGrass = CountSheep(sim);
        Assert.True(sheepGrass >= Constants.SHEEP_COUNT_MIN);

        sim.SetScene(Scene.Desert);
        Assert.Equal(sheepGrass, CountSheep(sim));
        Assert.Equal(CritterKind.Sheep, sim.CurrentCritter);

        sim.SetScene(Scene.Winter);
        Assert.Equal(sheepGrass, CountSheep(sim));
        Assert.Equal(CritterKind.Sheep, sim.CurrentCritter);
    }

    [Fact]
    public void ClickWithinStartleRadiusHopsSheepAway()
    {
        var sim = BuildSim();
        sim.SetCritter(CritterKind.Sheep);
        int idx = IndexOfFirstSheep(sim);
        Assert.True(idx >= 0);

        var before = sim.Entities[idx];
        // Pre-set age to verify reset.
        before.Age = 5.0;
        sim.Entities[idx] = before;

        // Click 16 DIP left of sheep, inside the cut band.
        double clickX = before.X - 16.0;
        double clickY = sim.WindowHeight - 20.0;
        sim.ApplyClick(clickX, clickY, 0.0);

        var after = sim.Entities[idx];
        Assert.Equal(Constants.SHEEP_STATE_HOPPING, after.State);
        Assert.Equal(Constants.SHEEP_HOP_DURATION, after.StateTimer, 9);
        Assert.Equal(0.0, after.Age, 9);
        Assert.True(after.Vx > 0.0); // sheep right of click → vx flipped to +
        Assert.True(System.Math.Abs(after.Vx)
                    <= Constants.SHEEP_WALK_SPEED_MAX * Constants.SHEEP_STARTLE_BOOST + 1e-9);
    }

    [Fact]
    public void ClickOutsideStartleRadiusLeavesSheepAlone()
    {
        var sim = BuildSim();
        sim.SetCritter(CritterKind.Sheep);
        int idx = IndexOfFirstSheep(sim);
        Assert.True(idx >= 0);

        var before = sim.Entities[idx];
        byte stateBefore = before.State;
        double vxBefore = before.Vx;

        double clickX = before.X + Constants.SHEEP_STARTLE_RADIUS + 200.0;
        double clickY = sim.WindowHeight - 20.0;
        sim.ApplyClick(clickX, clickY, 0.0);

        var after = sim.Entities[idx];
        Assert.Equal(stateBefore, after.State);
        Assert.Equal(vxBefore, after.Vx, 9);
    }
}
