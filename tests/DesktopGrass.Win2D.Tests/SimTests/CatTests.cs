// CatTests.cs - §17 Cat critter subsystem.
//
// Mirrors tests/DesktopGrass.Native.Tests/src/cat_tests.cpp.

using System.Linq;
using DesktopGrass.Win2D;
using Xunit;

namespace DesktopGrass.Win2D.Tests.SimTests;

public class CatTests
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

    private static int CountKind(Sim sim, EntityKind kind) =>
        sim.Entities.Count(e => e.Kind == kind);

    private static int IndexOfFirstKind(Sim sim, EntityKind kind)
    {
        for (int i = 0; i < sim.Entities.Count; i++)
            if (sim.Entities[i].Kind == kind) return i;
        return -1;
    }

    private static void KeepFirstCatOnly(Sim sim)
    {
        int idx = IndexOfFirstKind(sim, EntityKind.Cat);
        Assert.True(idx >= 0);
        var cat = sim.Entities[idx];
        sim.Entities.Clear();
        sim.Entities.Add(cat);
    }

    [Fact]
    public void CritterKindCatAndCritterCountArePinned()
    {
        Assert.Equal(0, (int)CritterKind.None);
        Assert.Equal(1, (int)CritterKind.Sheep);
        Assert.Equal(2, (int)CritterKind.Cat);
        Assert.Equal(3, (int)CritterKind.Bunny);
        Assert.Equal(4, Constants.CRITTER_COUNT);
        Assert.Equal(CritterKind.None, Constants.CRITTER_DEFAULT);
    }

    [Fact]
    public void EntityKindCatIsPinned()
    {
        Assert.Equal(0, (int)EntityKind.None);
        Assert.Equal(1, (int)EntityKind.Tumbleweed);
        Assert.Equal(2, (int)EntityKind.Snowflake);
        Assert.Equal(3, (int)EntityKind.Sheep);
        Assert.Equal(4, (int)EntityKind.Cat);
    }

    [Fact]
    public void CatConstantsArePinnedToSpecValues()
    {
        Assert.Equal(1, Constants.CAT_COUNT_MIN);
        Assert.Equal(2, Constants.CAT_COUNT_MAX);
        Assert.Equal(10.0, Constants.CAT_WALK_SPEED_MIN);
        Assert.Equal(22.0, Constants.CAT_WALK_SPEED_MAX);
        Assert.Equal(60.0, Constants.CAT_POUNCE_SPEED);

        Assert.Equal(11.0, Constants.CAT_BODY_RADIUS);
        Assert.Equal(7.0, Constants.CAT_BODY_HEIGHT);
        Assert.Equal(4.5, Constants.CAT_HEAD_RADIUS);
        Assert.Equal(5.0, Constants.CAT_LEG_LENGTH);
        Assert.Equal(13.0, Constants.CAT_TAIL_LENGTH);
        Assert.Equal(1.6, Constants.CAT_TAIL_THICKNESS);
        Assert.Equal(4.5, Constants.CAT_EAR_HEIGHT);

        Assert.Equal(0xFF6B6259u, Constants.CAT_BODY_COLOR);
        Assert.Equal(0xFF3D3733u, Constants.CAT_LEG_COLOR);
        Assert.Equal(0xFF6B6259u, Constants.CAT_FACE_COLOR);
        Assert.Equal(0xFF3D3733u, Constants.CAT_EAR_COLOR);
        Assert.Equal(0xFF1A1614u, Constants.CAT_INK_COLOR);

        Assert.Equal(0.50, Constants.CAT_WALK_PERIOD);
        Assert.Equal(1.6, Constants.CAT_LEG_CYCLE_AMP);
        Assert.Equal(0.4, Constants.CAT_HEAD_BOB_AMP);
        Assert.Equal(1.2, Constants.CAT_TAIL_SWAY_FREQ);
        Assert.Equal(0.35, Constants.CAT_TAIL_SWAY_AMP);

        Assert.Equal(Constants.SHEEP_STATE_WALKING, Constants.CAT_STATE_WALKING);
        Assert.Equal(Constants.SHEEP_STATE_IDLE, Constants.CAT_STATE_IDLE);
        Assert.Equal(Constants.SHEEP_STATE_SLEEPING, Constants.CAT_STATE_SLEEPING);
        Assert.Equal(Constants.SHEEP_STATE_HOPPING, Constants.CAT_STATE_POUNCING);

        Assert.Equal(6.0, Constants.CAT_WALK_DURATION_MIN);
        Assert.Equal(10.0, Constants.CAT_WALK_DURATION_MAX);
        Assert.Equal(4.0, Constants.CAT_IDLE_DURATION_MIN);
        Assert.Equal(8.0, Constants.CAT_IDLE_DURATION_MAX);
        Assert.Equal(20.0, Constants.CAT_SLEEP_DURATION_MIN);
        Assert.Equal(40.0, Constants.CAT_SLEEP_DURATION_MAX);
        Assert.Equal(0.45, Constants.CAT_POUNCE_DURATION);

        Assert.Equal(0.65, Constants.CAT_IDLE_PROBABILITY);
        Assert.Equal(0.30, Constants.CAT_SLEEP_PROBABILITY);
        Assert.Equal(0.50, Constants.CAT_SLEEP_FROM_IDLE_PROB_DEFAULT);
        Assert.Equal(0.20, Constants.CAT_SLEEP_FROM_IDLE_PROB_MORNING);
        Assert.Equal(0.85, Constants.CAT_SLEEP_FROM_IDLE_PROB_NIGHT);

        Assert.Equal(80.0, Constants.CAT_POUNCE_RADIUS);
        Assert.Equal(9.0, Constants.CAT_POUNCE_HEIGHT);
        Assert.Equal(100.0, Constants.CAT_CURIOUS_RADIUS);
        Assert.Equal(0.7, Constants.CAT_CURIOUS_HEAD_TURN_MAX);
    }

    [Fact]
    public void SimDefaultsToNoneAndDoesNotGenerateCatsUntilSelected()
    {
        var sim = BuildSim();
        Assert.Equal(CritterKind.None, sim.CurrentCritter);
        Assert.Equal(0, CountKind(sim, EntityKind.Cat));

        sim.SetCritter(CritterKind.Cat);
        Assert.True(CountKind(sim, EntityKind.Cat) >= Constants.CAT_COUNT_MIN);
    }

    [Fact]
    public void SetCritterCatProducesDeterministicCats()
    {
        var sim = BuildSim();
        sim.SetCritter(CritterKind.Cat);

        Assert.Equal(CritterKind.Cat, sim.CurrentCritter);
        int k = CountKind(sim, EntityKind.Cat);
        Assert.InRange(k, Constants.CAT_COUNT_MIN, Constants.CAT_COUNT_MAX);

        foreach (var e in sim.Entities)
        {
            if (e.Kind != EntityKind.Cat) continue;
            Assert.Equal(Constants.CAT_STATE_WALKING, e.State);
            Assert.InRange(e.StateTimer,
                Constants.CAT_WALK_DURATION_MIN,
                Constants.CAT_WALK_DURATION_MAX);
            double speed = System.Math.Abs(e.Vx);
            Assert.InRange(speed,
                Constants.CAT_WALK_SPEED_MIN,
                Constants.CAT_WALK_SPEED_MAX);
            double margin = e.Size + 8.0;
            Assert.InRange(e.X, margin, sim.MonitorWidth - margin);
            Assert.Equal(sim.WindowHeight - Constants.CAT_BODY_HEIGHT - Constants.CAT_LEG_LENGTH,
                         e.Y, 9);
            Assert.Equal(Constants.CAT_BODY_RADIUS, e.Size, 9);
            Assert.True(e.Lifetime < 0.0);
            Assert.True(e.NameIndex < Constants.CAT_NAME_POOL.Length);
            Assert.True(e.CoatVariantIndex < Constants.CAT_COAT_VARIANT_COUNT);
        }
    }

    [Fact]
    public void CatPrngDrawOrderMatchesSideStream()
    {
        // count, then per-cat: x, speed, dir-coin, seed, stateTimer, nameIndex, coatVariantIndex
        var side = Prng.Init(Constants.CANONICAL_TEST_SEED ^ Constants.CRITTER_PRNG_SALT);

        var sim = BuildSim();
        sim.SetCritter(CritterKind.Cat);

        double countDraw = side.Uniform(Constants.CAT_COUNT_MIN, Constants.CAT_COUNT_MAX + 1);
        int expectedCount = (int)System.Math.Floor(countDraw);
        if (expectedCount < Constants.CAT_COUNT_MIN) expectedCount = Constants.CAT_COUNT_MIN;
        if (expectedCount > Constants.CAT_COUNT_MAX) expectedCount = Constants.CAT_COUNT_MAX;
        Assert.Equal(expectedCount, CountKind(sim, EntityKind.Cat));

        int seen = 0;
        foreach (var e in sim.Entities)
        {
            if (e.Kind != EntityKind.Cat) continue;
            double margin = Constants.CAT_BODY_RADIUS + 8.0;
            double expectedX = side.Uniform(margin, Monitor1920 - margin);
            double expectedSpeed = side.Uniform(Constants.CAT_WALK_SPEED_MIN, Constants.CAT_WALK_SPEED_MAX);
            double dirCoin = side.Uniform(0.0, 1.0);
            double expectedDir = dirCoin < 0.5 ? -1.0 : 1.0;
            uint expectedSeed = side.NextU32();
            double expectedTimer = side.Uniform(Constants.CAT_WALK_DURATION_MIN,
                                                Constants.CAT_WALK_DURATION_MAX);
            byte expectedNameIndex = (byte)side.Index((uint)Constants.CAT_NAME_POOL.Length);
            byte expectedCoatVariantIndex = (byte)side.Index((uint)Constants.CAT_COAT_VARIANT_COUNT);

            Assert.Equal(expectedX, e.X, 9);
            Assert.Equal(expectedSpeed * expectedDir, e.Vx, 9);
            Assert.Equal(expectedSeed, e.Seed);
            Assert.Equal(expectedTimer, e.StateTimer, 9);
            Assert.Equal(expectedNameIndex, e.NameIndex);
            Assert.Equal(expectedCoatVariantIndex, e.CoatVariantIndex);
            seen++;
        }
        Assert.Equal(expectedCount, seen);
    }

    [Fact]
    public void SetCritterNoneClearsAmbientCats()
    {
        var sim = BuildSim();
        sim.SetCritter(CritterKind.Cat);
        Assert.True(CountKind(sim, EntityKind.Cat) >= Constants.CAT_COUNT_MIN);

        sim.SetCritter(CritterKind.None);
        Assert.Equal(CritterKind.None, sim.CurrentCritter);
        Assert.Equal(0, CountKind(sim, EntityKind.Cat));
        Assert.Equal(0, CountKind(sim, EntityKind.Bunny));
    }

    [Fact]
    public void SwitchingBetweenCritterSpeciesReplacesPriorSpecies()
    {
        var sim = BuildSim();
        sim.SetCritter(CritterKind.Cat);
        Assert.True(CountKind(sim, EntityKind.Cat) >= Constants.CAT_COUNT_MIN);
        Assert.Equal(0, CountKind(sim, EntityKind.Sheep));

        sim.SetCritter(CritterKind.Sheep);
        Assert.Equal(0, CountKind(sim, EntityKind.Cat));
        Assert.True(CountKind(sim, EntityKind.Sheep) >= Constants.SHEEP_COUNT_MIN);

        sim.SetCritter(CritterKind.Cat);
        Assert.Equal(0, CountKind(sim, EntityKind.Sheep));
        Assert.True(CountKind(sim, EntityKind.Cat) >= Constants.CAT_COUNT_MIN);
    }

    [Fact]
    public void SetSceneGatesActiveCatToGrass()
    {
        var sim = BuildSim();
        sim.SetCritter(CritterKind.Cat);
        int catsGrass = CountKind(sim, EntityKind.Cat);
        Assert.True(catsGrass >= Constants.CAT_COUNT_MIN);

        sim.SetScene(Scene.Desert);
        Assert.Equal(CritterKind.Cat, sim.CurrentCritter);
        Assert.Equal(0, CountKind(sim, EntityKind.Cat));

        sim.SetScene(Scene.Winter);
        Assert.Equal(CritterKind.Cat, sim.CurrentCritter);
        Assert.Equal(0, CountKind(sim, EntityKind.Cat));

        sim.SetScene(Scene.Grass);
        Assert.Equal(catsGrass, CountKind(sim, EntityKind.Cat));
    }

    [Fact]
    public void ClickWithinCatPounceRadiusPouncesTowardClick()
    {
        var sim = BuildSim();
        sim.SetCritter(CritterKind.Cat);
        KeepFirstCatOnly(sim);

        var cat = sim.Entities[0];
        cat.X = 500.0;
        cat.Vx = -Constants.CAT_WALK_SPEED_MIN;
        cat.Age = 5.0;
        sim.Entities[0] = cat;

        sim.ApplyClick(cat.X + 16.0, sim.WindowHeight - 20.0, 0.0);

        var after = sim.Entities[0];
        Assert.Equal(Constants.CAT_STATE_POUNCING, after.State);
        Assert.Equal(Constants.CAT_POUNCE_DURATION, after.StateTimer, 9);
        Assert.Equal(0.0, after.Age, 9);
        Assert.Equal(Constants.CAT_POUNCE_SPEED, after.Vx, 9);
    }

    [Fact]
    public void ClickOutsideCatPounceRadiusLeavesCatAlone()
    {
        var sim = BuildSim();
        sim.SetCritter(CritterKind.Cat);
        KeepFirstCatOnly(sim);

        var cat = sim.Entities[0];
        cat.X = 500.0;
        cat.Vx = -Constants.CAT_WALK_SPEED_MIN;
        sim.Entities[0] = cat;
        byte stateBefore = cat.State;
        double vxBefore = cat.Vx;

        sim.ApplyClick(cat.X + Constants.CAT_POUNCE_RADIUS + 5.0,
                       sim.WindowHeight - 20.0, 0.0);

        var after = sim.Entities[0];
        Assert.Equal(stateBefore, after.State);
        Assert.Equal(vxBefore, after.Vx, 9);
    }

    [Fact]
    public void CatsDoNotGreetOtherCats()
    {
        var sim = BuildSim();
        sim.SetCritter(CritterKind.Cat);
        KeepFirstCatOnly(sim);

        var first = sim.Entities[0];
        first.X = 400.0;
        first.Vx = Constants.CAT_WALK_SPEED_MIN;
        first.State = Constants.CAT_STATE_WALKING;
        first.StateTimer = 10.0;
        first.Age = Constants.SHEEP_GREET_MIN_AGE + 1.0;
        var second = first;
        second.X = first.X + 20.0;
        second.Vx = -Constants.CAT_WALK_SPEED_MIN;
        sim.Entities.Clear();
        sim.Entities.Add(first);
        sim.Entities.Add(second);

        sim.TickEntities(0.016);

        Assert.Equal(2, CountKind(sim, EntityKind.Cat));
        Assert.All(sim.Entities.Where(e => e.Kind == EntityKind.Cat),
            e => Assert.NotEqual(Constants.SHEEP_STATE_GREETING, e.State));
    }

    [Fact]
    public void CatsDoNotGreetSheep()
    {
        var sim = BuildSim();
        sim.SetCritter(CritterKind.Cat);
        KeepFirstCatOnly(sim);

        var cat = sim.Entities[0];
        cat.X = 400.0;
        cat.Vx = Constants.CAT_WALK_SPEED_MIN;
        cat.State = Constants.CAT_STATE_WALKING;
        cat.StateTimer = 10.0;
        cat.Age = Constants.SHEEP_GREET_MIN_AGE + 1.0;

        var sheep = new Entity
        {
            Kind = EntityKind.Sheep,
            Size = Constants.SHEEP_BODY_RADIUS,
            X = cat.X + 20.0,
            Y = sim.WindowHeight - Constants.SHEEP_BODY_HEIGHT - Constants.SHEEP_LEG_LENGTH,
            Vx = -Constants.SHEEP_WALK_SPEED_MIN,
            Age = Constants.SHEEP_GREET_MIN_AGE + 1.0,
            Lifetime = -1.0,
            State = Constants.SHEEP_STATE_WALKING,
            StateTimer = 10.0,
        };

        sim.Entities.Clear();
        sim.Entities.Add(cat);
        sim.Entities.Add(sheep);

        sim.TickEntities(0.016);

        Assert.Equal(1, CountKind(sim, EntityKind.Cat));
        Assert.Equal(1, CountKind(sim, EntityKind.Sheep));
        Assert.All(sim.Entities, e => Assert.NotEqual(Constants.SHEEP_STATE_GREETING, e.State));
    }

    [Fact]
    public void CatTimeOfDaySleepBiasIsPinned()
    {
        Assert.Equal(0.85, Sim.CatSleepProbForLocalHour(2));
        Assert.Equal(0.20, Sim.CatSleepProbForLocalHour(8));
        Assert.Equal(0.50, Sim.CatSleepProbForLocalHour(15));
        Assert.Equal(0.85, Sim.CatSleepProbForLocalHour(23));
    }
}
