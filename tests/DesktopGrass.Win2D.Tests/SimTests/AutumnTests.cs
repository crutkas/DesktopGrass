// AutumnTests.cs - §16.5 Autumn scene.

using DesktopGrass.Win2D;
using Xunit;

namespace DesktopGrass.Win2D.Tests.SimTests;

[Collection("Persistence state")]
public sealed class AutumnTests
{
    private const double Monitor1920 = 1920.0;
    private const double Epsilon = 1e-9;

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
        sim.ResetEntities(seed);
        return sim;
    }

    private static Sim BuildAutumnSim(ulong seed = Constants.CANONICAL_TEST_SEED,
                                      double monitorWidth = Monitor1920,
                                      double density = Constants.DEFAULT_DENSITY)
    {
        Sim sim = BuildSim(seed, monitorWidth, density);
        sim.SetScene(Scene.Autumn);
        return sim;
    }

    private static int CountKind(Sim sim, EntityKind kind) =>
        sim.Entities.Count(e => e.Kind == kind);

    private static int CountMaples(Sim sim) =>
        sim.Blades.Count(b => b.IsMaple);

    private static int CountNewLeafSpawns(Sim sim, double seconds, double dt = 0.05)
    {
        int count = 0;
        int steps = (int)Math.Ceiling(seconds / dt);
        for (int i = 0; i < steps; i++)
        {
            sim.Tick(dt, ReadOnlySpan<InputEvent>.Empty);
            count += sim.Entities.Count(e => e.Kind == EntityKind.Leaf && Math.Abs(e.Age) <= Epsilon);
        }
        return count;
    }

    private static Entity SpawnNextLeaf(Sim sim)
    {
        double dt = Math.Max(0.0, sim.NextLeafSpawnTime - sim.GlobalTime);
        sim.Tick(dt, ReadOnlySpan<InputEvent>.Empty);
        int index = sim.Entities.FindLastIndex(e => e.Kind == EntityKind.Leaf && Math.Abs(e.Age) <= Epsilon);
        Assert.True(index >= 0);
        return sim.Entities[index];
    }

    private static Sim BuildAutumnSimWithMaple(out ulong seed)
    {
        for (ulong offset = 0; offset < 512; offset++)
        {
            seed = Constants.CANONICAL_TEST_SEED + offset;
            Sim sim = BuildAutumnSim(seed);
            if (CountMaples(sim) > 0) return sim;
        }

        throw new InvalidOperationException("Unable to find deterministic seed with a maple");
    }

    private static Sim BuildAutumnSimWithMaple() => BuildAutumnSimWithMaple(out _);

    private static string UseStatePath()
    {
        string dir = Path.Combine(Directory.GetCurrentDirectory(), ".copilot-scratch", "win2d-autumn-tests");
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "state.json");
        Persistence.SetStateFilePathForTest(path);
        return path;
    }

    [Fact]
    public void SceneCountBumpsToFive()
    {
        Assert.Equal(5, Constants.SCENE_COUNT);
    }

    [Fact]
    public void AutumnSceneEnumValueIsPinned()
    {
        Assert.Equal(3, (int)Scene.Autumn);
    }

    [Fact]
    public void AutumnPaletteIsPinnedInScenePalettes()
    {
        for (int i = 0; i < Constants.PALETTE_SIZE; i++)
        {
            Assert.Equal(Constants.AUTUMN_PALETTE[i], Constants.SCENE_PALETTES[(int)Scene.Autumn, i]);
        }
    }

    [Fact]
    public void AutumnDoesNotChangeDefaultScene()
    {
        Assert.Equal(Scene.Grass, Constants.SCENE_DEFAULT);
    }

    [Fact]
    public void LeafConstantsMatchAutumnSpec()
    {
        Assert.Equal(1.4, Constants.LEAF_SPAWN_RATE_PER_SEC_1920DIP);
        Assert.Equal(14.0, Constants.LEAF_FALL_SPEED_MIN);
        Assert.Equal(26.0, Constants.LEAF_FALL_SPEED_MAX);
        Assert.Equal(32.0, Constants.LEAF_HORIZONTAL_DRIFT_AMP);
        Assert.Equal(1.4, Constants.LEAF_HORIZONTAL_DRIFT_FREQ);
        Assert.Equal(0.8, Constants.LEAF_ROTATION_SPEED_MIN);
        Assert.Equal(2.4, Constants.LEAF_ROTATION_SPEED_MAX);
        Assert.Equal(4.0, Constants.LEAF_SIZE_MIN);
        Assert.Equal(7.0, Constants.LEAF_SIZE_MAX);
        Assert.Equal(-10.0, Constants.LEAF_SPAWN_Y_OFFSET);
        Assert.Equal(6, Constants.LEAF_COLOR_COUNT);
        uint[] expected = [0xFFD96B0C, 0xFFB54D1E, 0xFFE89A3C, 0xFFC23E12, 0xFFE6C849, 0xFF8C2E0F];
        Assert.Equal(expected, Constants.LEAF_COLORS);
        Assert.Equal(0x1EA1DEC1D1EA1D05ul, Constants.LEAF_PRNG_SALT);
    }

    [Fact]
    public void AutumnLeafSpawnRateIsGatedAndNearMean()
    {
        Sim sim = BuildAutumnSim();
        int count = CountNewLeafSpawns(sim, 100.0);
        Assert.InRange(count, 112, 168);
    }

    [Fact]
    public void OnlyAutumnSpawnsLeaves()
    {
        foreach (Scene scene in new[] { Scene.Grass, Scene.Desert, Scene.Winter })
        {
            Sim sim = BuildSim();
            sim.SetScene(scene);
            CountNewLeafSpawns(sim, 30.0);
            Assert.Equal(0, CountKind(sim, EntityKind.Leaf));
        }
    }

    [Fact]
    public void LeafFallSpeedStaysWithinPinnedRange()
    {
        Sim sim = BuildAutumnSim();
        for (int i = 0; i < 32; i++)
        {
            Entity e = SpawnNextLeaf(sim);
            Assert.InRange(e.Vy, Constants.LEAF_FALL_SPEED_MIN, Constants.LEAF_FALL_SPEED_MAX);
            Assert.Equal(e.Vy, e.BaseSpeed, 9);
        }
    }

    [Fact]
    public void LeafSizeStaysWithinPinnedRange()
    {
        Sim sim = BuildAutumnSim();
        for (int i = 0; i < 32; i++)
        {
            Entity e = SpawnNextLeaf(sim);
            Assert.InRange(e.Size, Constants.LEAF_SIZE_MIN, Constants.LEAF_SIZE_MAX);
        }
    }

    [Fact]
    public void LeafColorVariantStaysWithinPinnedRange()
    {
        Sim sim = BuildAutumnSim();
        for (int i = 0; i < 32; i++)
        {
            Entity e = SpawnNextLeaf(sim);
            Assert.InRange(e.ColorVariant, (byte)0, (byte)(Constants.LEAF_COLOR_COUNT - 1));
        }
    }

    [Fact]
    public void LeafPrngDrawOrderMatchesSideStream()
    {
        Sim sim = BuildAutumnSim();
        var side = Prng.Init(Constants.CANONICAL_TEST_SEED ^ Constants.LEAF_PRNG_SALT);
        double lambda = Constants.LEAF_SPAWN_RATE_PER_SEC_1920DIP * sim.MonitorWidth / 1920.0;
        double expectedNext = 0.0;

        for (int i = 0; i < 8; i++)
        {
            Entity e = SpawnNextLeaf(sim);
            double xFrac = side.Uniform(0.0, 1.0);
            double expectedSpawnX = xFrac * sim.MonitorWidth;
            double expectedFallSpeed = side.Uniform(Constants.LEAF_FALL_SPEED_MIN, Constants.LEAF_FALL_SPEED_MAX);
            double expectedPhase = side.Uniform(0.0, 2.0 * Math.PI);
            double rotationMag = side.Uniform(Constants.LEAF_ROTATION_SPEED_MIN, Constants.LEAF_ROTATION_SPEED_MAX);
            double rotationSign = (side.NextU64() & 1UL) != 0UL ? 1.0 : -1.0;
            double expectedRotation = side.Uniform(0.0, 2.0 * Math.PI);
            double expectedSize = side.Uniform(Constants.LEAF_SIZE_MIN, Constants.LEAF_SIZE_MAX);
            byte expectedColor = (byte)side.Index((uint)Constants.LEAF_COLOR_COUNT);
            expectedNext += side.Exponential(lambda);

            Assert.Equal(expectedSpawnX, e.X0, 9);
            Assert.Equal(expectedSpawnX + Constants.LEAF_HORIZONTAL_DRIFT_AMP * Math.Sin(expectedPhase), e.X, 9);
            Assert.Equal(expectedFallSpeed, e.Vy, 9);
            Assert.Equal(expectedPhase, e.PhaseX, 9);
            Assert.Equal(rotationMag * rotationSign, e.RotationSpeed, 9);
            Assert.Equal(expectedRotation, e.Rotation, 9);
            Assert.Equal(expectedSize, e.Size, 9);
            Assert.Equal(expectedColor, e.ColorVariant);
            Assert.Equal(expectedNext, sim.NextLeafSpawnTime, 9);
        }
    }

    [Fact]
    public void LeafDespawnsWhenPastGround()
    {
        Sim sim = BuildAutumnSim();
        sim.Entities.Add(new Entity { Kind = EntityKind.Leaf, Y = sim.GroundY + 0.1, Lifetime = -1.0 });
        sim.NextLeafSpawnTime = 1.0e9;
        sim.TickEntities(0.0);
        Assert.Equal(0, CountKind(sim, EntityKind.Leaf));
    }

    [Fact]
    public void LeafIgnoresClickCutInteraction()
    {
        Sim sim = BuildAutumnSim();
        var leaf = new Entity { Kind = EntityKind.Leaf, X = 200.0, Y = sim.GroundY - 5.0, Size = 5.0, Lifetime = -1.0 };
        sim.Entities.Add(leaf);
        sim.ApplyClick(leaf.X, leaf.Y, sim.GlobalTime);
        Assert.Equal(1, CountKind(sim, EntityKind.Leaf));
    }

    [Fact]
    public void LeafEntityKindValueIsPinned()
    {
        Assert.Equal(11, (int)EntityKind.Leaf);
    }

    [Fact]
    public void MapleConstantsMatchAutumnSpec()
    {
        Assert.Equal(0.0070, Constants.MAPLE_PROBABILITY);
        Assert.Equal(50.0, Constants.MAPLE_HEIGHT_MIN);
        Assert.Equal(85.0, Constants.MAPLE_HEIGHT_MAX);
        Assert.Equal(6.0, Constants.MAPLE_TRUNK_WIDTH_MIN);
        Assert.Equal(10.0, Constants.MAPLE_TRUNK_WIDTH_MAX);
        Assert.Equal(14.0, Constants.MAPLE_CANOPY_RADIUS_MIN);
        Assert.Equal(24.0, Constants.MAPLE_CANOPY_RADIUS_MAX);
        Assert.Equal(0xFF4A2C18u, Constants.MAPLE_TRUNK_COLOR);
        Assert.Equal(0xFF2F1B0Eu, Constants.MAPLE_TRUNK_DARK);
        Assert.Equal(4, Constants.MAPLE_CANOPY_COLOR_COUNT);
        uint[] expected = [0xFFD96B0C, 0xFFE89A3C, 0xFFC23E12, 0xFFE6C849];
        Assert.Equal(expected, Constants.MAPLE_CANOPY_COLORS);
        Assert.Equal(0.20, Constants.MAPLE_BARE_FRACTION);
        Assert.Equal(0xC1AA51EC1AA51Eul, Constants.MAPLE_PRNG_SALT);
    }

    [Fact]
    public void MaplesGenerateOnlyInAutumn()
    {
        foreach (Scene scene in new[] { Scene.Grass, Scene.Desert, Scene.Winter })
        {
            Sim sim = BuildSim();
            sim.SetScene(scene);
            Assert.Equal(0, CountMaples(sim));
        }
        Assert.True(CountMaples(BuildAutumnSimWithMaple()) > 0);
    }

    [Fact]
    public void MaplePromotionProbabilityIsNearSpec()
    {
        int totalSlots = 0;
        int totalMaples = 0;
        for (ulong seed = Constants.CANONICAL_TEST_SEED; seed < Constants.CANONICAL_TEST_SEED + 200; seed++)
        {
            Sim sim = BuildAutumnSim(seed);
            totalSlots += sim.Blades.Length;
            totalMaples += CountMaples(sim);
        }
        double fraction = (double)totalMaples / totalSlots;
        Assert.InRange(fraction, Constants.MAPLE_PROBABILITY * 0.75, Constants.MAPLE_PROBABILITY * 1.25);
    }

    [Fact]
    public void MapleHeightStaysWithinPinnedRange()
    {
        Sim sim = BuildAutumnSimWithMaple();
        foreach (Blade b in sim.Blades.Where(b => b.IsMaple))
            Assert.InRange(b.MapleHeight, Constants.MAPLE_HEIGHT_MIN, Constants.MAPLE_HEIGHT_MAX);
    }

    [Fact]
    public void MapleTrunkWidthStaysWithinPinnedRange()
    {
        Sim sim = BuildAutumnSimWithMaple();
        foreach (Blade b in sim.Blades.Where(b => b.IsMaple))
            Assert.InRange(b.MapleTrunkWidth, Constants.MAPLE_TRUNK_WIDTH_MIN, Constants.MAPLE_TRUNK_WIDTH_MAX);
    }

    [Fact]
    public void MapleCanopyRadiusStaysWithinPinnedRange()
    {
        Sim sim = BuildAutumnSimWithMaple();
        foreach (Blade b in sim.Blades.Where(b => b.IsMaple))
            Assert.InRange(b.MapleCanopyRadius, Constants.MAPLE_CANOPY_RADIUS_MIN, Constants.MAPLE_CANOPY_RADIUS_MAX);
    }

    [Fact]
    public void MapleCanopyColorVariantStaysWithinPinnedRange()
    {
        Sim sim = BuildAutumnSimWithMaple();
        foreach (Blade b in sim.Blades.Where(b => b.IsMaple))
            Assert.InRange(b.MapleCanopyColorIdx, (byte)0, (byte)(Constants.MAPLE_CANOPY_COLOR_COUNT - 1));
    }

    [Fact]
    public void MapleBareFractionIsNearSpec()
    {
        int totalMaples = 0;
        int totalBare = 0;
        for (ulong seed = Constants.CANONICAL_TEST_SEED; seed < Constants.CANONICAL_TEST_SEED + 400; seed++)
        {
            Sim sim = BuildAutumnSim(seed);
            foreach (Blade b in sim.Blades.Where(b => b.IsMaple))
            {
                totalMaples++;
                if (b.MapleIsBare) totalBare++;
            }
        }
        Assert.True(totalMaples > 100);
        double fraction = (double)totalBare / totalMaples;
        Assert.InRange(fraction, Constants.MAPLE_BARE_FRACTION - 0.05, Constants.MAPLE_BARE_FRACTION + 0.05);
    }

    [Fact]
    public void MaplePrngDrawOrderMatchesSideStream()
    {
        Sim sim = BuildAutumnSimWithMaple(out ulong seed);
        var side = Prng.Init(seed ^ Constants.MAPLE_PRNG_SALT);

        for (int i = 0; i < sim.Blades.Length; i++)
        {
            double r = side.Uniform(0.0, 1.0);
            if (r >= Constants.MAPLE_PROBABILITY)
            {
                Assert.False(sim.Blades[i].IsMaple);
                continue;
            }

            double expectedHeight = side.Uniform(Constants.MAPLE_HEIGHT_MIN, Constants.MAPLE_HEIGHT_MAX);
            double expectedTrunkWidth = side.Uniform(Constants.MAPLE_TRUNK_WIDTH_MIN, Constants.MAPLE_TRUNK_WIDTH_MAX);
            double expectedCanopyRadius = side.Uniform(Constants.MAPLE_CANOPY_RADIUS_MIN, Constants.MAPLE_CANOPY_RADIUS_MAX);
            byte expectedColor = (byte)side.Index((uint)Constants.MAPLE_CANOPY_COLOR_COUNT);
            bool expectedBare = side.Uniform(0.0, 1.0) < Constants.MAPLE_BARE_FRACTION;

            Blade b = sim.Blades[i];
            Assert.True(b.IsMaple);
            Assert.Equal(expectedHeight, b.MapleHeight, 9);
            Assert.Equal(expectedTrunkWidth, b.MapleTrunkWidth, 9);
            Assert.Equal(expectedCanopyRadius, b.MapleCanopyRadius, 9);
            Assert.Equal(expectedColor, b.MapleCanopyColorIdx);
            Assert.Equal(expectedBare, b.MapleIsBare);
            return;
        }

        throw new InvalidOperationException("Expected a maple promotion");
    }

    [Fact]
    public void MaplesAreCuttableThroughExistingCutModel()
    {
        Sim sim = BuildAutumnSimWithMaple();
        Blade maple = sim.Blades.First(b => b.IsMaple);
        double clickX = maple.BaseX;
        sim.ApplyClick(clickX, sim.GroundY - 1.0, sim.GlobalTime);
        sim.Tick(Constants.CUT_DURATION_SEC + 0.01, ReadOnlySpan<InputEvent>.Empty);

        Blade cutMaple = sim.Blades.First(b => b.IsMaple && Math.Abs(b.BaseX - clickX) <= Epsilon);
        // Cut blades now settle at their per-blade stubble floor, not flat zero.
        Assert.True(cutMaple.CutFloor > 0.0);
        Assert.Equal(cutMaple.CutFloor, cutMaple.CutHeight, 9);
    }

    [Fact]
    public void AutumnIsCritterFree()
    {
        Sim sim = BuildAutumnSim();
        Assert.Equal(0, CountKind(sim, EntityKind.Sheep));
        Assert.Equal(0, CountKind(sim, EntityKind.Cat));
        Assert.Equal(0, CountKind(sim, EntityKind.Bunny));
        Assert.Equal(0, CountKind(sim, EntityKind.Hedgehog));
    }

    [Fact]
    public void AutumnDoesNotSpawnSnowflakes()
    {
        Sim sim = BuildAutumnSim();
        for (int i = 0; i < 500; i++) sim.Tick(0.05, ReadOnlySpan<InputEvent>.Empty);
        Assert.Equal(0, CountKind(sim, EntityKind.Snowflake));
    }

    [Fact]
    public void AutumnScenePersistsRoundTrip()
    {
        UseStatePath();
        var expected = new AppState(2, Scene.Autumn, CritterKind.None, 0, AutoStart: false, []);
        Persistence.Save(expected);
        AppState? actual = Persistence.Load();
        Assert.NotNull(actual);
        Assert.Equal(Scene.Autumn, actual.Scene);
    }

    [Fact]
    public void AutumnPrngSaltsAreUnique()
    {
        ulong[] salts =
        [
            Constants.REGROW_PRNG_SALT,
            Constants.FLOWER_PRNG_SALT,
            Constants.MUSHROOM_PRNG_SALT,
            Constants.AMBIENT_GUST_PRNG_SALT,
            Constants.CACTUS_PRNG_SALT,
            Constants.TUMBLEWEED_PRNG_SALT,
            Constants.CRITTER_PRNG_SALT,
            Constants.BUTTERFLY_PRNG_SALT,
            Constants.FIREFLY_PRNG_SALT,
            Constants.BIRD_FLYBY_PRNG_SALT,
            Constants.SNOWFLAKE_PRNG_SALT,
            Constants.PINE_PRNG_SALT,
            Constants.LEAF_PRNG_SALT,
            Constants.MAPLE_PRNG_SALT,
            Constants.LEAF_PUFF_PRNG_SALT,
        ];

        for (int i = 0; i < salts.Length; i++)
        for (int j = i + 1; j < salts.Length; j++)
            Assert.NotEqual(salts[i], salts[j]);
    }

    private static Sim BuildAutumnSimWithLeafyMaple()
    {
        for (ulong offset = 0; offset < 2048; offset++)
        {
            Sim sim = BuildAutumnSim(Constants.CANONICAL_TEST_SEED + offset);
            if (sim.Blades.Any(b => b.IsMaple && !b.MapleIsBare)) return sim;
        }

        throw new InvalidOperationException("Unable to find deterministic seed with a leafy maple");
    }

    private static Blade FirstLeafyMaple(Sim sim) =>
        sim.Blades.First(b => b.IsMaple && !b.MapleIsBare);

    [Fact]
    public void LeafPuffConstantsArePinned()
    {
        Assert.Equal(4, Constants.LEAF_PUFF_COUNT_MIN);
        Assert.Equal(7, Constants.LEAF_PUFF_COUNT_MAX);
        Assert.Equal(18.0, Constants.LEAF_PUFF_BURST_SPEED_MIN, 9);
        Assert.Equal(42.0, Constants.LEAF_PUFF_BURST_SPEED_MAX, 9);
        Assert.Equal(2.2, Constants.LEAF_PUFF_DRAG, 9);
        Assert.Equal(1.5, Constants.LEAF_PUFF_COOLDOWN_SEC, 9);
        Assert.Equal(1.15, Constants.LEAF_PUFF_HOVER_RADIUS_MUL, 9);
        Assert.Equal(0.5, Constants.LEAF_PUFF_MIN_CUT_HEIGHT, 9);
        Assert.Equal(0.4, Constants.LEAF_PUFF_START_OFFSET_FRAC, 9);
    }

    [Fact]
    public void HoveringLeafyMapleShedsPuff()
    {
        Sim sim = BuildAutumnSimWithLeafyMaple();
        Blade maple = FirstLeafyMaple(sim);
        double cx = maple.BaseX;
        double cy = sim.GroundY - maple.MapleHeight * maple.CutHeight;

        int before = CountKind(sim, EntityKind.Leaf);
        sim.ApplyCursorMove(new InputEvent(EventType.Move, cx, cy, sim.GlobalTime));

        int puffed = CountKind(sim, EntityKind.Leaf) - before;
        Assert.True(puffed >= Constants.LEAF_PUFF_COUNT_MIN);
        Assert.True(puffed <= Constants.LEAF_PUFF_COUNT_MAX);
        Assert.Contains(sim.Entities, e => e.Kind == EntityKind.Leaf && e.Vx != 0.0);
    }

    [Fact]
    public void LeafPuffRespectsCooldown()
    {
        Sim sim = BuildAutumnSimWithLeafyMaple();
        Blade maple = FirstLeafyMaple(sim);
        double cx = maple.BaseX;
        double cy = sim.GroundY - maple.MapleHeight * maple.CutHeight;

        sim.ApplyCursorMove(new InputEvent(EventType.Move, cx, cy, sim.GlobalTime));
        int afterFirst = CountKind(sim, EntityKind.Leaf);
        Assert.True(afterFirst > 0);

        sim.ApplyCursorMove(new InputEvent(EventType.Move, cx, cy, sim.GlobalTime));
        Assert.Equal(afterFirst, CountKind(sim, EntityKind.Leaf));

        sim.GlobalTime += Constants.LEAF_PUFF_COOLDOWN_SEC + 0.1;
        sim.ApplyCursorMove(new InputEvent(EventType.Move, cx, cy, sim.GlobalTime));
        Assert.True(CountKind(sim, EntityKind.Leaf) > afterFirst);
    }

    [Fact]
    public void LeafPuffIgnoresCursorAwayFromCanopy()
    {
        Sim sim = BuildAutumnSimWithLeafyMaple();
        Blade maple = FirstLeafyMaple(sim);
        int before = CountKind(sim, EntityKind.Leaf);

        double awayX = maple.BaseX + 400.0;
        double cy = sim.GroundY - maple.MapleHeight * maple.CutHeight;
        sim.ApplyCursorMove(new InputEvent(EventType.Move, awayX, cy, sim.GlobalTime));
        Assert.Equal(before, CountKind(sim, EntityKind.Leaf));
    }

    [Fact]
    public void LeafPuffDoesNotFireOutsideAutumn()
    {
        Sim sim = BuildAutumnSimWithLeafyMaple();
        Blade maple = FirstLeafyMaple(sim);
        double cx = maple.BaseX;
        double cy = sim.GroundY - maple.MapleHeight * maple.CutHeight;
        sim.CurrentScene = Scene.Grass;

        int before = CountKind(sim, EntityKind.Leaf);
        sim.ApplyCursorMove(new InputEvent(EventType.Move, cx, cy, sim.GlobalTime));
        Assert.Equal(before, CountKind(sim, EntityKind.Leaf));
    }

    [Fact]
    public void PuffBurstDecaysSoLeavesSettle()
    {
        Sim sim = BuildAutumnSimWithLeafyMaple();
        Blade maple = FirstLeafyMaple(sim);
        double cx = maple.BaseX;
        double cy = sim.GroundY - maple.MapleHeight * maple.CutHeight;
        sim.ApplyCursorMove(new InputEvent(EventType.Move, cx, cy, sim.GlobalTime));
        Assert.True(CountKind(sim, EntityKind.Leaf) > 0);

        for (int i = 0; i < 40; i++) sim.Tick(0.05, ReadOnlySpan<InputEvent>.Empty);
        foreach (var e in sim.Entities)
            if (e.Kind == EntityKind.Leaf)
                Assert.Equal(0.0, e.Vx, 9);
    }

    [Fact]
    public void ReEnteringAutumnClearsPuffCooldown()
    {
        Sim sim = BuildAutumnSimWithLeafyMaple();
        Blade maple = FirstLeafyMaple(sim);
        sim.ApplyCursorMove(new InputEvent(EventType.Move, maple.BaseX,
            sim.GroundY - maple.MapleHeight * maple.CutHeight, sim.GlobalTime));
        Assert.True(CountKind(sim, EntityKind.Leaf) > 0);

        // Leaving and re-entering Autumn regenerates the deterministic maples and
        // must reset their puff cooldown so the fresh scene can puff immediately.
        sim.SetScene(Scene.Grass);
        sim.SetScene(Scene.Autumn);
        Blade maple2 = FirstLeafyMaple(sim);

        int before = CountKind(sim, EntityKind.Leaf);
        sim.ApplyCursorMove(new InputEvent(EventType.Move, maple2.BaseX,
            sim.GroundY - maple2.MapleHeight * maple2.CutHeight, sim.GlobalTime));
        Assert.True(CountKind(sim, EntityKind.Leaf) > before);
    }
}
