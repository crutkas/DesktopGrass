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
