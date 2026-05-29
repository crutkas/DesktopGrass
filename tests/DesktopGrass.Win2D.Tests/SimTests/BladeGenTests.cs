// BladeGenTests.cs - §5 procedural generation.

using System.Linq;
using DesktopGrass.Win2D;
using Xunit;

namespace DesktopGrass.Win2D.Tests.SimTests;

public class BladeGenTests
{
    private const double Monitor1920 = 1920.0;

    [Fact]
    public void CanonicalSeedIsDeterministic()
    {
        var a = Sim.GenerateBlades(Constants.CANONICAL_TEST_SEED, Monitor1920, 1.0);
        var b = Sim.GenerateBlades(Constants.CANONICAL_TEST_SEED, Monitor1920, 1.0);

        Assert.Equal(a.Length, b.Length);
        for (int i = 0; i < a.Length; i++)
        {
            Assert.Equal(a[i].BaseX, b[i].BaseX);
            Assert.Equal(a[i].Height, b[i].Height);
            Assert.Equal(a[i].Thickness, b[i].Thickness);
            Assert.Equal(a[i].Hue, b[i].Hue);
            Assert.Equal(a[i].SwayPhaseOffset, b[i].SwayPhaseOffset);
            Assert.Equal(a[i].Stiffness, b[i].Stiffness);
            Assert.Equal(a[i].IsFlower, b[i].IsFlower);
            Assert.Equal(a[i].FlowerHeadColorIdx, b[i].FlowerHeadColorIdx);
            Assert.Equal(a[i].FlowerHeadRadius, b[i].FlowerHeadRadius);
            Assert.Equal(a[i].HeightBonus, b[i].HeightBonus);
            Assert.Equal(a[i].IsMushroom, b[i].IsMushroom);
            Assert.Equal(a[i].MushroomCapColorIdx, b[i].MushroomCapColorIdx);
            Assert.Equal(a[i].MushroomCapWidth, b[i].MushroomCapWidth);
            Assert.Equal(a[i].MushroomCapHeight, b[i].MushroomCapHeight);
            Assert.Equal(a[i].MushroomStemHeight, b[i].MushroomStemHeight);
            Assert.Equal(a[i].MushroomStemThickness, b[i].MushroomStemThickness);
        }
    }

    [Fact]
    public void FlowerFieldsAreDeterministicForCanonicalSeed()
    {
        var a = Sim.GenerateBlades(Constants.CANONICAL_TEST_SEED, Monitor1920, 1.0);
        var b = Sim.GenerateBlades(Constants.CANONICAL_TEST_SEED, Monitor1920, 1.0);

        Assert.Equal(a.Length, b.Length);
        for (int i = 0; i < a.Length; i++)
        {
            Assert.Equal(a[i].IsFlower, b[i].IsFlower);
            Assert.Equal(a[i].FlowerHeadColorIdx, b[i].FlowerHeadColorIdx);
            Assert.Equal(a[i].FlowerHeadRadius, b[i].FlowerHeadRadius);
            Assert.Equal(a[i].HeightBonus, b[i].HeightBonus);
        }
    }

    [Fact]
    public void BaseXIsStrictlyIncreasing()
    {
        var blades = Sim.GenerateBlades(Constants.CANONICAL_TEST_SEED, Monitor1920, 1.0);
        Assert.NotEmpty(blades);
        for (int i = 1; i < blades.Length; i++)
        {
            Assert.True(blades[i].BaseX > blades[i - 1].BaseX,
                $"baseX not strictly increasing at i={i}: {blades[i - 1].BaseX} vs {blades[i].BaseX}");
        }
    }

    [Fact]
    public void AllFieldsWithinSpecRanges()
    {
        var blades = Sim.GenerateBlades(Constants.CANONICAL_TEST_SEED, Monitor1920, 1.0);
        foreach (ref readonly var b in blades.AsSpan())
        {
            Assert.InRange(b.BaseX, 0.0, Monitor1920);
            Assert.InRange(b.Height, Constants.BLADE_HEIGHT_MIN, Constants.BLADE_HEIGHT_MAX);
            Assert.InRange(b.Thickness, Constants.BLADE_THICKNESS_MIN, Constants.BLADE_THICKNESS_MAX);
            Assert.InRange(b.Hue, (byte)0, (byte)(Constants.PALETTE_SIZE - 1));
            Assert.InRange(b.SwayPhaseOffset, 0.0, 2.0 * System.Math.PI);
            Assert.InRange(b.Stiffness, Constants.STIFFNESS_MIN, Constants.STIFFNESS_MAX);

            Assert.Equal(1.0, b.CutHeight);
            Assert.Equal(0.0, b.GustVelocity);
            Assert.Equal(-1.0, b.CutAnimStart);
            Assert.Equal(1.0, b.CutInitialHeight);

            if (b.IsFlower)
            {
                Assert.InRange(b.FlowerHeadColorIdx, (byte)0, (byte)(Constants.FLOWER_PALETTE_SIZE - 1));
                Assert.InRange(b.FlowerHeadRadius, Constants.FLOWER_HEAD_RADIUS_MIN, Constants.FLOWER_HEAD_RADIUS_MAX);
                Assert.InRange(b.HeightBonus, Constants.FLOWER_HEIGHT_BONUS_MIN, Constants.FLOWER_HEIGHT_BONUS_MAX);
            }
            else
            {
                Assert.Equal((byte)0, b.FlowerHeadColorIdx);
                Assert.Equal(0.0, b.FlowerHeadRadius);
                Assert.Equal(1.0, b.HeightBonus);
            }

            if (b.IsMushroom)
            {
                Assert.InRange(b.MushroomCapColorIdx, (byte)0, (byte)(Constants.MUSHROOM_PALETTE_SIZE - 1));
                Assert.InRange(b.MushroomCapWidth, Constants.MUSHROOM_CAP_WIDTH_MIN, Constants.MUSHROOM_CAP_WIDTH_MAX);
                Assert.InRange(b.MushroomCapHeight, Constants.MUSHROOM_CAP_HEIGHT_MIN, Constants.MUSHROOM_CAP_HEIGHT_MAX);
                Assert.InRange(b.MushroomStemHeight, Constants.MUSHROOM_STEM_HEIGHT_MIN, Constants.MUSHROOM_STEM_HEIGHT_MAX);
                Assert.InRange(b.MushroomStemThickness, Constants.MUSHROOM_STEM_THICKNESS_MIN, Constants.MUSHROOM_STEM_THICKNESS_MAX);
            }
            else
            {
                Assert.Equal((byte)0, b.MushroomCapColorIdx);
                Assert.Equal(0.0, b.MushroomCapWidth);
                Assert.Equal(0.0, b.MushroomCapHeight);
                Assert.Equal(0.0, b.MushroomStemHeight);
                Assert.Equal(0.0, b.MushroomStemThickness);
            }
        }
    }

    [Fact]
    public void DensityScalesBladeCount()
    {
        var d10 = Sim.GenerateBlades(Constants.CANONICAL_TEST_SEED, Monitor1920, 1.0);
        var d20 = Sim.GenerateBlades(Constants.CANONICAL_TEST_SEED, Monitor1920, 2.0);
        Assert.True(d20.Length > d10.Length * 1.6, $"d20={d20.Length}, d10={d10.Length}");
    }

    [Fact]
    public void BladeCountSensibleAt1920DipDensity1()
    {
        var blades = Sim.GenerateBlades(Constants.CANONICAL_TEST_SEED, Monitor1920, 1.0);
        Assert.InRange(blades.Length, 260, 400);
    }

    [Fact]
    public void HueCoversAllSixIndicesAtTypicalDensity()
    {
        var blades = Sim.GenerateBlades(Constants.CANONICAL_TEST_SEED, Monitor1920, 1.0);
        var hues = blades.Select(b => (int)b.Hue).Distinct().OrderBy(x => x).ToArray();
        Assert.Equal(Constants.PALETTE_SIZE, hues.Length);
    }

    [Fact]
    public void StaticFieldDrawOrderIsPinned()
    {
        var blades = Sim.GenerateBlades(Constants.CANONICAL_TEST_SEED, Monitor1920, 1.0);
        Assert.NotEmpty(blades);
        var first = blades[0];

        var rng = Prng.Init(Constants.CANONICAL_TEST_SEED);
        double expectedStep = rng.Uniform(Constants.BLADE_SPACING_MIN, Constants.BLADE_SPACING_MAX);
        double expectedHeight = rng.Uniform(Constants.BLADE_HEIGHT_MIN, Constants.BLADE_HEIGHT_MAX);
        double expectedThickness = rng.Uniform(Constants.BLADE_THICKNESS_MIN, Constants.BLADE_THICKNESS_MAX);
        uint expectedHue = rng.Index(Constants.PALETTE_SIZE);
        double expectedPhase = rng.Uniform(0.0, 2.0 * System.Math.PI);
        double expectedStiffness = rng.Uniform(Constants.STIFFNESS_MIN, Constants.STIFFNESS_MAX);

        Assert.Equal(expectedStep, first.BaseX, 12);
        Assert.Equal(expectedHeight, first.Height, 12);
        Assert.Equal(expectedThickness, first.Thickness, 12);
        Assert.Equal((byte)expectedHue, first.Hue);
        Assert.Equal(expectedPhase, first.SwayPhaseOffset, 12);
        Assert.Equal(expectedStiffness, first.Stiffness, 12);
    }
}
