// SheepGreetingTests.cs - §16 sheep proximity-greeting tests.
//
// Mirrors tests/DesktopGrass.Native.Tests/src/sheep_greeting_tests.cpp.

using System;
using System.Collections.Generic;
using System.Linq;
using DesktopGrass.Win2D;
using Xunit;

namespace DesktopGrass.Win2D.Tests.SimTests;

public class SheepGreetingTests
{
    private const double Monitor1920 = 1920.0;
    private const double EligibleAge = 2.0;
    private const double LongTimer = 10.0;

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
        sim.SetCritter(CritterKind.Sheep);
        return sim;
    }

    private static List<int> SheepIndices(Sim sim)
    {
        var indices = new List<int>();
        for (int i = 0; i < sim.Entities.Count; i++)
        {
            if (sim.Entities[i].Kind == EntityKind.Sheep) indices.Add(i);
        }
        return indices;
    }

    private static void SetSheep(Sim sim, int index, double x, double vx,
                                 byte state = Constants.SHEEP_STATE_WALKING,
                                 double age = EligibleAge,
                                 double stateTimer = LongTimer)
    {
        Entity e = sim.Entities[index];
        e.X = x;
        e.Vx = vx;
        e.State = state;
        e.Age = age;
        e.StateTimer = stateTimer;
        sim.Entities[index] = e;
    }

    private static List<int> PrepareTwoSheep(Sim sim, double gap = 40.0,
                                             double ageA = EligibleAge,
                                             double ageB = EligibleAge)
    {
        List<int> indices = SheepIndices(sim);
        Assert.True(indices.Count >= 2);

        SetSheep(sim, indices[0], 500.0, -20.0, Constants.SHEEP_STATE_WALKING, ageA);
        SetSheep(sim, indices[1], 500.0 + gap, 18.0, Constants.SHEEP_STATE_WALKING, ageB);
        for (int n = 2; n < indices.Count; n++)
        {
            SetSheep(sim, indices[n], 1000.0 + 150.0 * n, 16.0);
        }
        return indices;
    }

    private static int AdvanceSidePastSheepGeneration(ref Prng side)
    {
        double countDraw = side.Uniform(Constants.SHEEP_COUNT_MIN, Constants.SHEEP_COUNT_MAX + 1);
        int expectedCount = (int)Math.Floor(countDraw);
        if (expectedCount < Constants.SHEEP_COUNT_MIN) expectedCount = Constants.SHEEP_COUNT_MIN;
        if (expectedCount > Constants.SHEEP_COUNT_MAX) expectedCount = Constants.SHEEP_COUNT_MAX;

        for (int i = 0; i < expectedCount; i++)
        {
            double margin = Constants.SHEEP_BODY_RADIUS + 8.0;
            _ = side.Uniform(margin, Monitor1920 - margin);
            _ = side.Uniform(Constants.SHEEP_WALK_SPEED_MIN, Constants.SHEEP_WALK_SPEED_MAX);
            _ = side.Uniform(0.0, 1.0);
            _ = side.NextU32();
            _ = side.Uniform(Constants.SHEEP_WALK_DURATION_MIN, Constants.SHEEP_WALK_DURATION_MAX);
        }
        return expectedCount;
    }

    [Fact]
    public void SheepGreetingConstantsArePinnedToSpecValues()
    {
        Assert.Equal((byte)5, Constants.SHEEP_STATE_GREETING);
        Assert.Equal(50.0, Constants.SHEEP_GREET_RADIUS);
        Assert.Equal(1.6, Constants.SHEEP_GREET_DURATION_MIN);
        Assert.Equal(2.8, Constants.SHEEP_GREET_DURATION_MAX);
        Assert.Equal(1.5, Constants.SHEEP_GREET_MIN_AGE);
        Assert.Equal(4.5, Constants.SHEEP_GREET_HEAD_BOB_FREQ);
        Assert.Equal(0.7, Constants.SHEEP_GREET_HEAD_BOB_AMP);
    }

    [Fact]
    public void SheepCuriousConstantsArePinnedToSpecValues()
    {
        Assert.Equal(80.0, Constants.SHEEP_CURIOUS_RADIUS);
        Assert.Equal(0.55, Constants.SHEEP_CURIOUS_HEAD_TURN_MAX);
    }

    [Theory]
    [InlineData(2, 0.70)]
    [InlineData(5, 0.70)]
    [InlineData(6, 0.10)]
    [InlineData(9, 0.10)]
    [InlineData(10, 0.30)]
    [InlineData(15, 0.30)]
    [InlineData(21, 0.30)]
    [InlineData(22, 0.70)]
    [InlineData(23, 0.70)]
    public void SheepSleepProbabilityFollowsLocalHourBands(int hour, double expected)
    {
        Assert.Equal(expected, Sim.SheepSleepProbForLocalHour(hour));
    }

    [Fact]
    public void EligibleNearbySheepEnterGreetingFacingEachOther()
    {
        Sim sim = BuildSim();
        List<int> indices = PrepareTwoSheep(sim);

        sim.TickEntities(0.016);

        Entity a = sim.Entities[indices[0]];
        Entity b = sim.Entities[indices[1]];
        Assert.Equal(Constants.SHEEP_STATE_GREETING, a.State);
        Assert.Equal(Constants.SHEEP_STATE_GREETING, b.State);
        Assert.InRange(a.StateTimer, Constants.SHEEP_GREET_DURATION_MIN, Constants.SHEEP_GREET_DURATION_MAX);
        Assert.Equal(a.StateTimer, b.StateTimer, 12);
        Assert.True(a.Vx > 0.0);
        Assert.True(b.Vx < 0.0);
    }

    [Fact]
    public void FarApartEligibleSheepDoNotGreet()
    {
        Sim sim = BuildSim();
        List<int> indices = PrepareTwoSheep(sim, 200.0);

        for (int i = 0; i < 3; i++) sim.TickEntities(0.016);

        Assert.Equal(Constants.SHEEP_STATE_WALKING, sim.Entities[indices[0]].State);
        Assert.Equal(Constants.SHEEP_STATE_WALKING, sim.Entities[indices[1]].State);
    }

    [Fact]
    public void SheepUnderGreetingMinimumAgeDoNotGreet()
    {
        Sim sim = BuildSim();
        List<int> indices = PrepareTwoSheep(sim, 40.0, 0.5, EligibleAge);

        sim.TickEntities(0.016);

        Assert.Equal(Constants.SHEEP_STATE_WALKING, sim.Entities[indices[0]].State);
        Assert.Equal(Constants.SHEEP_STATE_WALKING, sim.Entities[indices[1]].State);
    }

    [Fact]
    public void SleepingHoppingAndGreetingSheepAreNotGreetingEligible()
    {
        byte[] blockedStates =
        {
            Constants.SHEEP_STATE_SLEEPING,
            Constants.SHEEP_STATE_HOPPING,
            Constants.SHEEP_STATE_GREETING,
        };

        foreach (byte blockedState in blockedStates)
        {
            Sim sim = BuildSim();
            List<int> indices = PrepareTwoSheep(sim);
            SetSheep(sim, indices[0], 500.0, -20.0, blockedState, EligibleAge);

            sim.TickEntities(0.016);

            Assert.Equal(blockedState, sim.Entities[indices[0]].State);
            Assert.Equal(Constants.SHEEP_STATE_WALKING, sim.Entities[indices[1]].State);
        }
    }

    [Fact]
    public void GreetingExpiryReturnsSheepToWalkingWithVxFlipped()
    {
        Sim sim = BuildSim();
        List<int> indices = PrepareTwoSheep(sim);

        sim.TickEntities(0.016);
        double duration = sim.Entities[indices[0]].StateTimer;
        double aGreetingVx = sim.Entities[indices[0]].Vx;
        double bGreetingVx = sim.Entities[indices[1]].Vx;

        sim.TickEntities(duration + 0.01);

        Assert.Equal(Constants.SHEEP_STATE_WALKING, sim.Entities[indices[0]].State);
        Assert.Equal(Constants.SHEEP_STATE_WALKING, sim.Entities[indices[1]].State);
        Assert.Equal(-aGreetingVx, sim.Entities[indices[0]].Vx, 12);
        Assert.Equal(-bGreetingVx, sim.Entities[indices[1]].Vx, 12);
    }

    [Fact]
    public void GreetingTriggerConsumesOnePrngDrawPerPair()
    {
        var side = Prng.Init(Constants.CANONICAL_TEST_SEED ^ Constants.CRITTER_PRNG_SALT);

        Sim sim = BuildSim();
        int expectedCount = AdvanceSidePastSheepGeneration(ref side);
        Assert.Equal(expectedCount, SheepIndices(sim).Count);
        List<int> indices = PrepareTwoSheep(sim);

        double expectedDuration = side.Uniform(
            Constants.SHEEP_GREET_DURATION_MIN,
            Constants.SHEEP_GREET_DURATION_MAX);
        sim.TickEntities(0.016);

        Assert.Equal(expectedDuration, sim.Entities[indices[0]].StateTimer, 12);
        Assert.Equal(expectedDuration, sim.Entities[indices[1]].StateTimer, 12);
    }

    [Fact]
    public void SingleSheepCannotEnterGreeting()
    {
        Sim sim = BuildSim();
        Assert.True(sim.Entities.Count >= 1);
        sim.Entities.RemoveRange(1, sim.Entities.Count - 1);
        SetSheep(sim, 0, 500.0, 20.0);

        sim.TickEntities(0.016);

        Assert.Single(sim.Entities);
        Assert.Equal(Constants.SHEEP_STATE_WALKING, sim.Entities[0].State);
    }

    [Fact]
    public void ThreeSheepClusterGreetsOnlyTheFirstEncounteredPair()
    {
        Sim sim = BuildSim();
        List<int> indices = SheepIndices(sim);
        Assert.True(indices.Count >= 2);
        if (indices.Count < 3)
        {
            sim.Entities.Add(sim.Entities[indices[1]]);
            indices = SheepIndices(sim);
        }
        Assert.True(indices.Count >= 3);

        SetSheep(sim, indices[0], 500.0, -20.0);
        SetSheep(sim, indices[1], 540.0, 18.0);
        SetSheep(sim, indices[2], 580.0, 16.0);

        sim.TickEntities(0.016);

        Assert.Equal(Constants.SHEEP_STATE_GREETING, sim.Entities[indices[0]].State);
        Assert.Equal(Constants.SHEEP_STATE_GREETING, sim.Entities[indices[1]].State);
        Assert.Equal(Constants.SHEEP_STATE_WALKING, sim.Entities[indices[2]].State);
        Assert.Equal(2, sim.Entities.Count(e => e.Kind == EntityKind.Sheep
                                                && e.State == Constants.SHEEP_STATE_GREETING));
    }
}
