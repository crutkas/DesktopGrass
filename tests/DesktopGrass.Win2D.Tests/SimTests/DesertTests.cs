// DesertTests.cs - §14 Desert scene cacti + tumbleweeds.

using System;
using System.Linq;
using DesktopGrass.Win2D;
using Xunit;

namespace DesktopGrass.Win2D.Tests.SimTests;

public class DesertTests
{
    private const double Monitor1920 = 1920.0;

    private readonly struct ExpectedCactus
    {
        public readonly int SlotIndex;
        public readonly byte Type;
        public readonly double Height;
        public readonly double Width;
        public readonly sbyte ArmSide;

        public ExpectedCactus(int slotIndex, byte type, double height, double width, sbyte armSide)
        {
            SlotIndex = slotIndex;
            Type = type;
            Height = height;
            Width = width;
            ArmSide = armSide;
        }
    }

    private static Sim BuildSim(double density = Constants.DEFAULT_DENSITY)
    {
        var sim = new Sim
        {
            Blades = Sim.GenerateBlades(Constants.CANONICAL_TEST_SEED, Monitor1920, density),
            WindowHeight = Constants.STRIP_HEIGHT + Constants.HEADROOM,
            GroundY = Constants.STRIP_HEIGHT + Constants.HEADROOM,
        };
        sim.ResetAmbientGusts(Constants.CANONICAL_TEST_SEED, Monitor1920);
        sim.ResetEntities(Constants.CANONICAL_TEST_SEED);
        return sim;
    }

    private static ExpectedCactus FirstExpectedCactus(int bladeCount)
    {
        var prng = Prng.Init(Constants.CANONICAL_TEST_SEED ^ Constants.CACTUS_PRNG_SALT);
        for (int i = 0; i < bladeCount; i++)
        {
            double r = prng.Uniform(0.0, 1.0);
            if (r >= Constants.CACTUS_PROBABILITY) continue;

            double height = prng.Uniform(Constants.CACTUS_HEIGHT_MIN, Constants.CACTUS_HEIGHT_MAX);
            double width = prng.Uniform(Constants.CACTUS_WIDTH_MIN, Constants.CACTUS_WIDTH_MAX);
            double armDraw = prng.Uniform(0.0, 1.0);
            double noArmThreshold = 1.0 - Constants.CACTUS_ARM_PROBABILITY;
            double twoArmThreshold = noArmThreshold + Constants.CACTUS_TWO_ARM_PROBABILITY * Constants.CACTUS_ARM_PROBABILITY;
            if (armDraw < noArmThreshold)
            {
                return new ExpectedCactus(i, 0, height, width, +1);
            }
            if (armDraw < twoArmThreshold)
            {
                return new ExpectedCactus(i, 2, height, width, +1);
            }

            sbyte side = prng.Uniform(0.0, 1.0) < 0.5 ? (sbyte)-1 : (sbyte)+1;
            return new ExpectedCactus(i, 1, height, width, side);
        }

        throw new InvalidOperationException("canonical seed produced no cactus slot");
    }

    private static int ExpectedTumbleweedCount(double monitorWidth)
    {
        if (monitorWidth < 480.0) return 0;
        int count = (int)Math.Floor(monitorWidth / 1920.0 * Constants.TUMBLEWEED_COUNT_PER_1920DIP);
        return count < 1 ? 1 : count;
    }

    [Fact]
    public void DesertConstantsArePinned()
    {
        Assert.Equal(0.005, Constants.CACTUS_PROBABILITY);
        Assert.Equal(30.0, Constants.CACTUS_HEIGHT_MIN);
        Assert.Equal(70.0, Constants.CACTUS_HEIGHT_MAX);
        Assert.Equal(0xFF2D7A2Du, Constants.CACTUS_COLOR);
        Assert.Equal(4, Constants.TUMBLEWEED_COUNT_PER_1920DIP);
        Assert.Equal(120.0, Constants.TUMBLEWEED_SPEED_MAX);
        Assert.Equal(0x7B0117CA7B0117CAul, Constants.TUMBLEWEED_PRNG_SALT);
    }

    [Fact]
    public void SetSceneDesertClearsEntitiesAndGeneratesCacti()
    {
        var sim = BuildSim();
        sim.Entities.Add(new Entity { Kind = EntityKind.Snowflake });

        sim.SetScene(Scene.Desert);

        Assert.Equal(Scene.Desert, sim.CurrentScene);
        Assert.Equal(ExpectedTumbleweedCount(Monitor1920), sim.Entities.Count);
        Assert.All(sim.Entities, e => Assert.Equal(EntityKind.Tumbleweed, e.Kind));

        int cactusCount = sim.Blades.Count(b => b.IsCactus);
        Assert.InRange(cactusCount, 1, 10);
    }

    [Fact]
    public void FirstCactusMatchesSpecDerivedPrngSnapshot()
    {
        var sim = BuildSim();
        var expected = FirstExpectedCactus(sim.Blades.Length);

        sim.SetScene(Scene.Desert);

        Blade b = sim.Blades[expected.SlotIndex];
        Assert.True(b.IsCactus);
        Assert.Equal(expected.Type, b.CactusType);
        Assert.Equal(expected.Height, b.CactusHeight, 12);
        Assert.Equal(expected.Width, b.CactusWidth, 12);
        if (expected.Type == 1) Assert.Equal(expected.ArmSide, b.CactusArmSide);
    }

    [Fact]
    public void GrassSceneRestoresOriginalFlowerAndMushroomSlotVariants()
    {
        var sim = BuildSim();
        var expected = FirstExpectedCactus(sim.Blades.Length);

        sim.Blades[expected.SlotIndex].IsFlower = true;
        sim.Blades[expected.SlotIndex].IsMushroom = true;
        sim.Blades[expected.SlotIndex].OriginalIsFlower = true;
        sim.Blades[expected.SlotIndex].OriginalIsMushroom = true;

        sim.SetScene(Scene.Desert);
        Assert.True(sim.Blades[expected.SlotIndex].IsCactus);
        Assert.False(sim.Blades[expected.SlotIndex].IsFlower);
        Assert.False(sim.Blades[expected.SlotIndex].IsMushroom);

        sim.SetScene(Scene.Grass);
        Assert.False(sim.Blades[expected.SlotIndex].IsCactus);
        Assert.True(sim.Blades[expected.SlotIndex].IsFlower);
        Assert.True(sim.Blades[expected.SlotIndex].IsMushroom);
    }

    [Fact]
    public void DesertGeneratesExpectedTumbleweedCount()
    {
        var sim = BuildSim();
        sim.SetScene(Scene.Desert);

        int expected = ExpectedTumbleweedCount(Monitor1920);
        Assert.True(expected >= 1);
        Assert.Equal(expected, sim.Entities.Count);
    }

    [Fact]
    public void FirstTumbleweedMatchesSpecDerivedPrngSnapshot()
    {
        var sim = BuildSim();
        sim.SetScene(Scene.Desert);
        Assert.NotEmpty(sim.Entities);

        var prng = Prng.Init(Constants.CANONICAL_TEST_SEED ^ Constants.TUMBLEWEED_PRNG_SALT);
        double expectedSize = prng.Uniform(Constants.TUMBLEWEED_SIZE_MIN, Constants.TUMBLEWEED_SIZE_MAX);
        double expectedX = prng.Uniform(0.0, Monitor1920);
        double expectedY = sim.GroundY - prng.Uniform(Constants.TUMBLEWEED_Y_OFFSET_MIN, Constants.TUMBLEWEED_Y_OFFSET_MAX);
        double speed = prng.Uniform(Constants.TUMBLEWEED_SPEED_MIN, Constants.TUMBLEWEED_SPEED_MAX);
        double direction = prng.Uniform(0.0, 1.0) < 0.5 ? -1.0 : 1.0;
        double expectedVx = direction * speed;
        double expectedRotation = prng.Uniform(0.0, 2.0 * Math.PI);

        Entity e = sim.Entities[0];
        Assert.Equal(EntityKind.Tumbleweed, e.Kind);
        Assert.Equal(expectedSize, e.Size, 12);
        Assert.Equal(expectedX, e.X, 12);
        Assert.Equal(expectedY, e.Y, 12);
        Assert.Equal(expectedVx, e.Vx, 12);
        Assert.Equal(expectedRotation, e.Rotation, 12);
        Assert.Equal(expectedVx / expectedSize, e.RotationSpeed, 12);
    }

    [Fact]
    public void TumbleweedRespawnsAtOppositeEdgeWhenOffScreen()
    {
        var sim = BuildSim();
        sim.SetScene(Scene.Desert);
        Assert.NotEmpty(sim.Entities);

        Entity e = sim.Entities[0];
        e.X = sim.MonitorWidth + 100.0;
        sim.Entities[0] = e;

        sim.TickEntities(0.0);

        e = sim.Entities[0];
        Assert.Equal(EntityKind.Tumbleweed, e.Kind);
        Assert.Equal(-e.Size, e.X, 12);
        Assert.True(e.Vx > 0.0);
    }

    [Fact]
    public void DesertSceneLeavesCanonicalFirstBladeGeometryBitIdentical()
    {
        var sim = BuildSim(density: 1.0);
        sim.SetScene(Scene.Desert);

        Blade first = sim.Blades[0];
        Assert.Equal(4.941073726820111, first.BaseX, 12);
        Assert.Equal(24.469991818248864, first.Height, 12);
        Assert.Equal(1.5829214329729786, first.Thickness, 12);
        Assert.Equal((byte)3, first.Hue);
        Assert.Equal(3.3176304956845826, first.SwayPhaseOffset, 12);
        Assert.Equal(0.97444439458772458, first.Stiffness, 12);
    }
}
