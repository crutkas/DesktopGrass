// PineTests.cs - §15.1 Winter pine trees (slot-bound, mirrors §14 cacti).

using System;
using DesktopGrass.Win2D;
using Xunit;

namespace DesktopGrass.Win2D.Tests.SimTests;

public class PineTests
{
    private const double Monitor1920 = 1920.0;

    private readonly struct ExpectedPine
    {
        public readonly int SlotIndex;
        public readonly double Height;
        public readonly double Width;
        public readonly int TierCount;

        public ExpectedPine(int slotIndex, double height, double width, int tierCount)
        {
            SlotIndex = slotIndex;
            Height = height;
            Width = width;
            TierCount = tierCount;
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

    private static ExpectedPine FirstExpectedPine(int bladeCount)
    {
        var prng = Prng.Init(Constants.CANONICAL_TEST_SEED ^ Constants.PINE_PRNG_SALT);
        for (int i = 0; i < bladeCount; i++)
        {
            double r = prng.Uniform(0.0, 1.0);
            if (r >= Constants.PINE_PROBABILITY) continue;

            double height = prng.Uniform(Constants.PINE_HEIGHT_MIN, Constants.PINE_HEIGHT_MAX);
            double width = prng.Uniform(Constants.PINE_WIDTH_MIN, Constants.PINE_WIDTH_MAX);
            double tierDraw = prng.Uniform(Constants.PINE_TIER_COUNT_MIN, Constants.PINE_TIER_COUNT_MAX + 1);
            int tiers = (int)Math.Floor(tierDraw);
            if (tiers < Constants.PINE_TIER_COUNT_MIN) tiers = Constants.PINE_TIER_COUNT_MIN;
            if (tiers > Constants.PINE_TIER_COUNT_MAX) tiers = Constants.PINE_TIER_COUNT_MAX;
            return new ExpectedPine(i, height, width, tiers);
        }
        throw new InvalidOperationException("canonical seed produced no pine slot");
    }

    [Fact]
    public void PineConstantsArePinned()
    {
        Assert.Equal(0.006, Constants.PINE_PROBABILITY);
        Assert.Equal(45.0, Constants.PINE_HEIGHT_MIN);
        Assert.Equal(90.0, Constants.PINE_HEIGHT_MAX);
        Assert.Equal(16.0, Constants.PINE_WIDTH_MIN);
        Assert.Equal(28.0, Constants.PINE_WIDTH_MAX);
        Assert.Equal(2, Constants.PINE_TIER_COUNT_MIN);
        Assert.Equal(4, Constants.PINE_TIER_COUNT_MAX);
        Assert.Equal(0.25, Constants.PINE_TIP_TAPER);
        Assert.Equal(0.15, Constants.PINE_TIER_OVERLAP);
        Assert.Equal(0.30, Constants.PINE_SNOW_CAP_FRACTION);
        Assert.Equal(0xFF1B5E20u, Constants.PINE_COLOR);
        Assert.Equal(0x50494E4550494E45ul, Constants.PINE_PRNG_SALT);
    }

    [Fact]
    public void SetSceneWinterPromotesSomeSlotsToPines()
    {
        var sim = BuildSim();
        sim.SetScene(Scene.Winter);

        int pineCount = 0;
        foreach (var b in sim.Blades)
        {
            if (b.IsPine)
            {
                pineCount++;
                Assert.InRange(b.PineTierCount, Constants.PINE_TIER_COUNT_MIN, Constants.PINE_TIER_COUNT_MAX);
                Assert.InRange(b.PineHeight, Constants.PINE_HEIGHT_MIN, Constants.PINE_HEIGHT_MAX);
                Assert.InRange(b.PineWidth, Constants.PINE_WIDTH_MIN, Constants.PINE_WIDTH_MAX);
            }
        }
        Assert.InRange(pineCount, 1, 20);
    }

    [Fact]
    public void FirstPineMatchesSpecDerivedPrngSnapshot()
    {
        var sim = BuildSim();
        var expected = FirstExpectedPine(sim.Blades.Length);

        sim.SetScene(Scene.Winter);

        Assert.InRange(expected.SlotIndex, 0, sim.Blades.Length - 1);
        var b = sim.Blades[expected.SlotIndex];
        Assert.True(b.IsPine);
        Assert.Equal(expected.TierCount, b.PineTierCount);
        Assert.Equal(expected.Height, b.PineHeight, 12);
        Assert.Equal(expected.Width, b.PineWidth, 12);
    }

    [Fact]
    public void GrassSceneRestoresPineSlotsToVanillaVariants()
    {
        var sim = BuildSim();
        var expected = FirstExpectedPine(sim.Blades.Length);
        Assert.InRange(expected.SlotIndex, 0, sim.Blades.Length - 1);

        sim.Blades[expected.SlotIndex].IsFlower = true;
        sim.Blades[expected.SlotIndex].IsMushroom = true;
        sim.Blades[expected.SlotIndex].OriginalIsFlower = true;
        sim.Blades[expected.SlotIndex].OriginalIsMushroom = true;

        sim.SetScene(Scene.Winter);
        Assert.True(sim.Blades[expected.SlotIndex].IsPine);
        Assert.False(sim.Blades[expected.SlotIndex].IsFlower);
        Assert.False(sim.Blades[expected.SlotIndex].IsMushroom);

        sim.SetScene(Scene.Grass);
        Assert.False(sim.Blades[expected.SlotIndex].IsPine);
        Assert.True(sim.Blades[expected.SlotIndex].IsFlower);
        Assert.True(sim.Blades[expected.SlotIndex].IsMushroom);
    }

    [Fact]
    public void WinterSceneSuppressesMushroomsOnEverySlot()
    {
        var sim = BuildSim();
        for (int i = 0; i < sim.Blades.Length; i += 17)
        {
            sim.Blades[i].IsMushroom = true;
            sim.Blades[i].OriginalIsMushroom = true;
        }

        sim.SetScene(Scene.Winter);

        foreach (var b in sim.Blades) Assert.False(b.IsMushroom);

        sim.SetScene(Scene.Grass);
        Assert.Equal(sim.Blades[0].OriginalIsMushroom, sim.Blades[0].IsMushroom);
    }

    [Fact]
    public void WinterGrassHeightScaleIsPinned()
    {
        Assert.Equal(0.5, Constants.WINTER_GRASS_HEIGHT_SCALE);
    }

    [Fact]
    public void WinterSceneLeavesCanonicalFirstBladeBitIdentical()
    {
        var sim = BuildSim(density: 1.0);
        var before = sim.Blades[0];

        sim.SetScene(Scene.Winter);

        var after = sim.Blades[0];
        Assert.Equal(before.BaseX, after.BaseX, 12);
        Assert.Equal(before.Height, after.Height, 12);
        Assert.Equal(before.Thickness, after.Thickness, 12);
        Assert.Equal(before.Hue, after.Hue);
        Assert.Equal(before.SwayPhaseOffset, after.SwayPhaseOffset, 12);
        Assert.Equal(before.Stiffness, after.Stiffness, 12);
    }
}
