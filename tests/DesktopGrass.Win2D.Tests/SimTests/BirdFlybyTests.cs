// BirdFlybyTests.cs - §17.8 ambient bird flyby tests.

using System;
using System.Collections.Generic;
using System.Linq;
using DesktopGrass.Win2D;
using Xunit;

namespace DesktopGrass.Win2D.Tests.SimTests;

public class BirdFlybyTests
{
    private const double Monitor1920 = 1920.0;

    private static Sim BuildSim(ulong seed = Constants.CANONICAL_TEST_SEED)
    {
        var sim = new Sim
        {
            Blades = Sim.GenerateBlades(seed, Monitor1920, Constants.DEFAULT_DENSITY),
            WindowHeight = Constants.STRIP_HEIGHT + Constants.HEADROOM,
            GroundY = Constants.STRIP_HEIGHT + Constants.HEADROOM,
            CurrentScene = Scene.Grass,
        };
        sim.ResetAmbientGusts(seed, Monitor1920);
        sim.ResetEntities(seed);
        sim.Entities.Clear();
        return sim;
    }

    private static int CountBirds(Sim sim) => sim.Entities.Count(e => e.Kind == EntityKind.Bird);
    private static List<Entity> Birds(Sim sim) => sim.Entities.Where(e => e.Kind == EntityKind.Bird).ToList();

    private static int PrngCount(ref Prng side, int minCount, int maxCount)
    {
        double draw = side.Uniform(minCount, maxCount + 1);
        int count = (int)Math.Floor(draw);
        if (count < minCount) count = minCount;
        if (count > maxCount) count = maxCount;
        return count;
    }

    private static void ResetBirdStreamFresh(Sim sim, ulong seed)
    {
        sim.BirdFlybyPrng = Prng.Init(seed ^ Constants.BIRD_FLYBY_PRNG_SALT);
        sim.NextBirdFlybyAtTime = sim.GlobalTime;
    }

    private static void ResetBirdSchedule(Sim sim, ulong seed)
    {
        sim.BirdFlybyPrng = Prng.Init(seed ^ Constants.BIRD_FLYBY_PRNG_SALT);
        sim.NextBirdFlybyAtTime = sim.GlobalTime + Sim.BirdFlybySampleInterval(ref sim.BirdFlybyPrng);
    }

    private static ulong FindVSeed(int minSize)
    {
        for (ulong i = 1; i < 10000; i++)
        {
            ulong seed = Constants.CANONICAL_TEST_SEED + i;
            var sim = BuildSim(seed);
            ResetBirdStreamFresh(sim, seed);
            sim.SpawnBirdFlyby();
            var flock = Birds(sim);
            if (flock.Count >= minSize && flock[0].ColorVariant == 0) return seed;
        }
        return Constants.CANONICAL_TEST_SEED;
    }

    [Fact]
    public void BirdFlybyConstantsArePinnedToSpecValues()
    {
        Assert.Equal(15.0, Constants.BIRD_FLYBY_SPAWN_RATE_PER_HOUR);
        Assert.Equal(7, Constants.BIRD_FLYBY_HOUR_START);
        Assert.Equal(19, Constants.BIRD_FLYBY_HOUR_END);
        Assert.Equal(3, Constants.BIRD_FLOCK_SIZE_MIN);
        Assert.Equal(7, Constants.BIRD_FLOCK_SIZE_MAX);
        Assert.Equal(9.0, Constants.BIRD_FLOCK_FORMATION_SPACING);
        Assert.Equal(22.0, Constants.BIRD_FLOCK_V_ANGLE_DEG);
        Assert.Equal(65.0, Constants.BIRD_SPEED_MIN);
        Assert.Equal(95.0, Constants.BIRD_SPEED_MAX);
        Assert.Equal(78.0, Constants.BIRD_ALTITUDE_MIN);
        Assert.Equal(96.0, Constants.BIRD_ALTITUDE_MAX);
        Assert.Equal(3.6, Constants.BIRD_BODY_LENGTH);
        Assert.Equal(5.0, Constants.BIRD_WING_SPAN);
        Assert.Equal(7.0, Constants.BIRD_WING_FLAP_FREQ);
        Assert.Equal(0.6, Constants.BIRD_WING_FLAP_PHASE_JITTER);
        Assert.Equal(0xFF1A1610u, Constants.BIRD_BODY_COLOR);
        Assert.Equal(1.0, Constants.BIRD_WING_OPEN_RATIO);
        Assert.Equal(0.30, Constants.BIRD_WING_FOLD_RATIO);
        Assert.Equal(0.08, Constants.BIRD_FADE_IN_FRAC);
        Assert.Equal(0.08, Constants.BIRD_FADE_OUT_FRAC);
        Assert.Equal(3.0, Constants.BIRD_DRIFT_AMP_Y);
        Assert.Equal(0.8, Constants.BIRD_DRIFT_FREQ_Y);
        Assert.Equal(0xB12D1F1A1B12D1Aul, Constants.BIRD_FLYBY_PRNG_SALT);
    }

    [Fact]
    public void BirdFlybyPrngSaltIsUnique()
    {
        ulong[] salts =
        {
            Constants.REGROW_PRNG_SALT, Constants.FLOWER_PRNG_SALT, Constants.MUSHROOM_PRNG_SALT,
            Constants.AMBIENT_GUST_PRNG_SALT, Constants.CACTUS_PRNG_SALT, Constants.TUMBLEWEED_PRNG_SALT,
            Constants.CRITTER_PRNG_SALT, Constants.BUTTERFLY_PRNG_SALT, Constants.FIREFLY_PRNG_SALT,
            Constants.SNOWFLAKE_PRNG_SALT, Constants.PINE_PRNG_SALT,
        };
        Assert.All(salts, salt => Assert.NotEqual(Constants.BIRD_FLYBY_PRNG_SALT, salt));
    }

    [Fact]
    public void BirdFlybyFlockSizeStaysInRangeOverSeeds()
    {
        for (ulong i = 0; i < 256; i++)
        {
            ulong seed = unchecked(Constants.CANONICAL_TEST_SEED + i * 0x9E3779B97F4A7C15UL);
            var sim = BuildSim(seed);
            ResetBirdStreamFresh(sim, seed);
            sim.SpawnBirdFlyby();
            Assert.InRange(CountBirds(sim), Constants.BIRD_FLOCK_SIZE_MIN, Constants.BIRD_FLOCK_SIZE_MAX);
        }
    }

    [Fact]
    public void BirdFlybyLeaderAltitudeStaysInRange()
    {
        for (ulong i = 0; i < 128; i++)
        {
            ulong seed = Constants.CANONICAL_TEST_SEED + i;
            var sim = BuildSim(seed);
            ResetBirdStreamFresh(sim, seed);
            sim.SpawnBirdFlyby();
            var flock = Birds(sim);
            Assert.NotEmpty(flock);
            Assert.InRange(flock[0].AltitudeAnchor, Constants.BIRD_ALTITUDE_MIN, Constants.BIRD_ALTITUDE_MAX);
        }
    }

    [Fact]
    public void BirdFlybyLeaderSpeedStaysInRange()
    {
        for (ulong i = 0; i < 128; i++)
        {
            ulong seed = Constants.CANONICAL_TEST_SEED + i;
            var sim = BuildSim(seed);
            ResetBirdStreamFresh(sim, seed);
            sim.SpawnBirdFlyby();
            var flock = Birds(sim);
            Assert.NotEmpty(flock);
            Assert.InRange(flock[0].BaseSpeed, Constants.BIRD_SPEED_MIN, Constants.BIRD_SPEED_MAX);
            Assert.Equal(flock[0].BaseSpeed, Math.Abs(flock[0].Vx), 9);
        }
    }

    [Fact]
    public void BirdFlybyPrngDrawOrderMatchesSideStream()
    {
        const ulong seed = 0xB17D5EED1234UL;
        var sim = BuildSim(seed);
        ResetBirdStreamFresh(sim, seed);
        var side = Prng.Init(seed ^ Constants.BIRD_FLYBY_PRNG_SALT);

        sim.SpawnBirdFlyby();
        int expectedCount = PrngCount(ref side, Constants.BIRD_FLOCK_SIZE_MIN, Constants.BIRD_FLOCK_SIZE_MAX);
        ulong directionBit = side.NextU64() & 1UL;
        double direction = directionBit != 0UL ? 1.0 : -1.0;
        double leaderAltitude = side.Uniform(Constants.BIRD_ALTITUDE_MIN, Constants.BIRD_ALTITUDE_MAX);
        double leaderSpeed = side.Uniform(Constants.BIRD_SPEED_MIN, Constants.BIRD_SPEED_MAX);
        ulong formationStyle = side.NextU64() & 1UL;

        var wingPhases = new List<double>();
        var driftPhases = new List<double>();
        for (int i = 0; i < expectedCount; i++)
        {
            wingPhases.Add(side.Uniform(-Constants.BIRD_WING_FLAP_PHASE_JITTER, Constants.BIRD_WING_FLAP_PHASE_JITTER));
            driftPhases.Add(side.Uniform(0.0, 2.0 * Math.PI));
        }

        var flock = Birds(sim);
        Assert.Equal(expectedCount, flock.Count);
        Assert.Equal(side.State, sim.BirdFlybyPrng.State);

        double spawnX = direction > 0.0 ? -50.0 : Monitor1920 + 50.0;
        double sinAngle = Math.Sin(Constants.BIRD_FLOCK_V_ANGLE_DEG * Math.PI / 180.0);
        for (int i = 0; i < expectedCount; i++)
        {
            double along = -i * Constants.BIRD_FLOCK_FORMATION_SPACING;
            double perpendicular;
            if (formationStyle == 0UL)
            {
                int armIndex = (i + 1) / 2;
                double sideSign = (i % 2) == 0 ? 1.0 : -1.0;
                perpendicular = sideSign * armIndex * Constants.BIRD_FLOCK_FORMATION_SPACING * sinAngle;
            }
            else
            {
                perpendicular = i * Constants.BIRD_FLOCK_FORMATION_SPACING * sinAngle;
            }

            Entity e = flock[i];
            Assert.Equal(spawnX + direction * along, e.X0, 9);
            Assert.Equal(e.X0, e.X, 9);
            Assert.Equal(direction * leaderSpeed, e.Vx, 9);
            Assert.Equal(leaderSpeed, e.BaseSpeed, 9);
            Assert.Equal(leaderAltitude - perpendicular, e.AltitudeAnchor, 9);
            Assert.Equal(wingPhases[i], e.PhaseX, 9);
            Assert.Equal(driftPhases[i], e.PhaseY, 9);
            Assert.Equal(along, e.FormationOffsetAlongFlight, 9);
            Assert.Equal(perpendicular, e.FormationOffsetPerpendicular, 9);
            Assert.Equal((byte)formationStyle, e.ColorVariant);
            Assert.Equal(sim.GlobalTime, e.SpawnTime, 9);
        }
    }

    [Fact]
    public void BirdFlybysAreGrassSceneOnly()
    {
        foreach (Scene scene in new[] { Scene.Desert, Scene.Winter })
        {
            var sim = BuildSim();
            sim.SetScene(scene);
            sim.Entities.Clear();
            ResetBirdSchedule(sim, Constants.CANONICAL_TEST_SEED);
            for (int i = 0; i < 8 * 3600; i++)
            {
                sim.GlobalTime += 1.0;
                sim.TickBirdFlybys(12);
            }
            Assert.Equal(0, CountBirds(sim));
        }
    }

    [Fact]
    public void BirdFlybyDayGatingControlsPoissonSpawns()
    {
        var night = BuildSim();
        ResetBirdSchedule(night, Constants.CANONICAL_TEST_SEED);
        for (int i = 0; i < 10 * 3600; i++)
        {
            night.GlobalTime += 1.0;
            night.TickBirdFlybys(2);
        }
        Assert.Equal(0, CountBirds(night));

        const ulong seed = 0xDAD1B17DUL;
        var day = BuildSim(seed);
        ResetBirdSchedule(day, seed);
        int flybys = 0;
        for (int i = 0; i < 10 * 3600; i++)
        {
            day.GlobalTime += 1.0;
            int before = CountBirds(day);
            day.TickBirdFlybys(12);
            if (CountBirds(day) > before)
            {
                flybys++;
                day.Entities.Clear();
            }
        }

        double observedPerHour = flybys / 10.0;
        Assert.InRange(observedPerHour,
            Constants.BIRD_FLYBY_SPAWN_RATE_PER_HOUR * 0.85,
            Constants.BIRD_FLYBY_SPAWN_RATE_PER_HOUR * 1.15);
    }

    [Fact]
    public void BirdVFormationGeometryIsLocked()
    {
        ulong seed = FindVSeed(5);
        var sim = BuildSim(seed);
        ResetBirdStreamFresh(sim, seed);
        sim.SpawnBirdFlyby();
        var flock = Birds(sim);
        Assert.True(flock.Count >= 5);
        Assert.Equal(0, flock[0].ColorVariant);
        Assert.Equal(0.0, flock[0].FormationOffsetAlongFlight, 9);

        for (int i = 1; i < flock.Count; i++)
        {
            Assert.True(Math.Abs(flock[0].FormationOffsetAlongFlight) < Math.Abs(flock[i].FormationOffsetAlongFlight));
            Assert.Equal(Constants.BIRD_FLOCK_FORMATION_SPACING,
                flock[i - 1].FormationOffsetAlongFlight - flock[i].FormationOffsetAlongFlight, 9);
            double expectedSign = (i % 2 == 0) ? 1.0 : -1.0;
            Assert.Equal(expectedSign, flock[i].FormationOffsetPerpendicular > 0.0 ? 1.0 : -1.0);
        }
    }

    [Fact]
    public void BirdWingFlapScaleStaysInRange()
    {
        for (int i = 0; i < 200; i++)
        {
            double t = i * 0.137;
            double phase = -Constants.BIRD_WING_FLAP_PHASE_JITTER
                + (2.0 * Constants.BIRD_WING_FLAP_PHASE_JITTER) * i / 199.0;
            double scale = Constants.BirdWingScale(t, phase);
            Assert.InRange(scale, Constants.BIRD_WING_FOLD_RATIO, Constants.BIRD_WING_OPEN_RATIO);
        }
    }

    [Fact]
    public void BirdWingPhasesDecorrelateWithinFlock()
    {
        for (ulong i = 1; i < 10000; i++)
        {
            ulong seed = Constants.CANONICAL_TEST_SEED + i;
            var sim = BuildSim(seed);
            ResetBirdStreamFresh(sim, seed);
            sim.SpawnBirdFlyby();
            var flock = Birds(sim);
            if (flock.Count != 5) continue;

            var distinct = new List<double>();
            foreach (Entity e in flock)
            {
                double scale = Constants.BirdWingScale(1.234, e.PhaseX);
                if (!distinct.Any(existing => Math.Abs(existing - scale) < 1e-6))
                {
                    distinct.Add(scale);
                }
            }
            if (distinct.Count >= 3)
            {
                Assert.True(distinct.Count >= 3);
                return;
            }
        }
        Assert.Fail("no decorrelated 5-bird flock found");
    }

    [Fact]
    public void BirdsDespawnPastOppositeBoundary()
    {
        var sim = BuildSim();
        sim.CurrentScene = Scene.Desert;
        sim.Entities.Clear();
        sim.Entities.Add(new Entity
        {
            Kind = EntityKind.Bird,
            X = Monitor1920 + 49.0,
            Y = 10.0,
            Vx = 20.0,
            AltitudeAnchor = Constants.BIRD_ALTITUDE_MIN,
            Lifetime = -1.0,
        });

        sim.TickEntities(0.2);

        Assert.Equal(0, CountBirds(sim));
    }

    [Fact]
    public void BirdsDoNotInteractWithCutsOrCritters()
    {
        var sim = BuildSim();
        sim.Entities.Clear();
        var bird = new Entity
        {
            Kind = EntityKind.Bird,
            X = 500.0,
            Y = sim.WindowHeight - Constants.STRIP_HEIGHT - 10.0,
            Vx = Constants.BIRD_SPEED_MIN,
            BaseSpeed = Constants.BIRD_SPEED_MIN,
            AltitudeAnchor = Constants.BIRD_ALTITUDE_MIN,
            Lifetime = -1.0,
        };
        sim.Entities.Add(bird);
        sim.Entities.Add(new Entity
        {
            Kind = EntityKind.Sheep,
            X = bird.X,
            Y = sim.WindowHeight - Constants.SHEEP_BODY_HEIGHT - Constants.SHEEP_LEG_LENGTH,
            Vx = Constants.SHEEP_WALK_SPEED_MIN,
            State = Constants.SHEEP_STATE_WALKING,
            StateTimer = 10.0,
        });

        sim.ApplyClick(bird.X, bird.Y, 0.0);

        Assert.Equal(EntityKind.Bird, sim.Entities[0].Kind);
        Assert.Equal(Constants.BIRD_SPEED_MIN, sim.Entities[0].BaseSpeed, 9);
        Assert.Equal(Constants.SHEEP_STATE_WALKING, sim.Entities[1].State);
        Assert.All(sim.Blades, b => Assert.True(b.CutAnimStart < 0.0));
    }

    [Fact]
    public void BirdFlybyPoissonInterArrivalsKeepExpectedMean()
    {
        const ulong seed = 0x510B17D00UL;
        var sim = BuildSim(seed);
        ResetBirdSchedule(sim, seed);

        double prev = sim.GlobalTime;
        double totalInterval = 0.0;
        const int eventsCount = 100;
        for (int i = 0; i < eventsCount; i++)
        {
            sim.GlobalTime = sim.NextBirdFlybyAtTime;
            totalInterval += sim.GlobalTime - prev;
            prev = sim.GlobalTime;
            sim.TickBirdFlybys(12);
            sim.Entities.Clear();
        }

        double expectedMean = 3600.0 / Constants.BIRD_FLYBY_SPAWN_RATE_PER_HOUR;
        double observedMean = totalInterval / eventsCount;
        Assert.InRange(observedMean, expectedMean * 0.80, expectedMean * 1.20);
    }
}
