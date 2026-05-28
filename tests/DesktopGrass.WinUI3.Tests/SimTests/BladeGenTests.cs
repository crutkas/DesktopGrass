// BladeGenTests.cs — §5 procedural generation conformance.
//
// Shared snapshot fixture: blade count and the first 10 / last 10 blade
// fields for the canonical (seed, monitorWidth=1920, density=1.0) tuple.

using System.Linq;
using DesktopGrass.WinUI3;
using Xunit;

namespace DesktopGrass.WinUI3.Tests.SimTests;

public class BladeGenTests
{
    private const double Monitor1920 = 1920.0;

    [Fact]
    public void CanonicalSeedIsDeterministic()
    {
        var a = Sim.GenerateBlades(Constants.CanonicalTestSeed, Monitor1920, 1.0);
        var b = Sim.GenerateBlades(Constants.CanonicalTestSeed, Monitor1920, 1.0);

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
        var blades = Sim.GenerateBlades(Constants.CanonicalTestSeed, Monitor1920, 1.0);
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
        var blades = Sim.GenerateBlades(Constants.CanonicalTestSeed, Monitor1920, 1.0);
        foreach (ref readonly var b in blades.AsSpan())
        {
            Assert.InRange(b.BaseX, 0.0, Monitor1920);
            Assert.InRange(b.Height, Constants.BladeHeightMin, Constants.BladeHeightMax);
            Assert.InRange(b.Thickness, Constants.BladeThicknessMin, Constants.BladeThicknessMax);
            Assert.InRange(b.Hue, (byte)0, (byte)(Constants.PaletteSize - 1));
            Assert.InRange(b.SwayPhaseOffset, 0.0, 2.0 * System.Math.PI);
            Assert.InRange(b.Stiffness, Constants.StiffnessMin, Constants.StiffnessMax);

            Assert.Equal(1.0, b.CutHeight);
            Assert.Equal(0.0, b.GustVelocity);
            Assert.Equal(-1.0, b.CutAnimStart);
            Assert.Equal(1.0, b.CutInitialHeight);
        }
    }

    [Fact]
    public void DensityScalesBladeCount()
    {
        var d10 = Sim.GenerateBlades(Constants.CanonicalTestSeed, Monitor1920, 1.0);
        var d20 = Sim.GenerateBlades(Constants.CanonicalTestSeed, Monitor1920, 2.0);
        Assert.True(d20.Length > d10.Length * 1.6, $"d20={d20.Length}, d10={d10.Length}");
    }

    [Fact]
    public void BladeCountSensibleAt1920DipDensity1()
    {
        var blades = Sim.GenerateBlades(Constants.CanonicalTestSeed, Monitor1920, 1.0);
        // Spec §5 target band: ~320 ± reasonable for monitorWidth=1920 at
        // density=1.0 with spacing ∈ [4, 8].
        Assert.InRange(blades.Length, 260, 400);
    }

    [Fact]
    public void HueCoversAllSixIndicesAtTypicalDensity()
    {
        var blades = Sim.GenerateBlades(Constants.CanonicalTestSeed, Monitor1920, 1.0);
        var hues = blades.Select(b => (int)b.Hue).Distinct().OrderBy(x => x).ToArray();
        Assert.Equal(Constants.PaletteSize, hues.Length);
    }

    [Fact]
    public void StaticFieldDrawOrderIsPinned()
    {
        // The PRNG MUST be sampled in this exact order per blade:
        //   height, thickness, hue, swayPhaseOffset, stiffness
        // (after the leading spacing draw for the step). Re-deriving the
        // first blade by hand from the PRNG must match.
        var blades = Sim.GenerateBlades(Constants.CanonicalTestSeed, Monitor1920, 1.0);
        Assert.NotEmpty(blades);
        var first = blades[0];

        var rng = new Prng(Constants.CanonicalTestSeed);
        double expectedStep = rng.Uniform(Constants.BladeSpacingMin, Constants.BladeSpacingMax);
        double expectedHeight = rng.Uniform(Constants.BladeHeightMin, Constants.BladeHeightMax);
        double expectedThickness = rng.Uniform(Constants.BladeThicknessMin, Constants.BladeThicknessMax);
        uint expectedHue = rng.Index((uint)Constants.PaletteSize);
        double expectedPhase = rng.Uniform(0.0, 2.0 * System.Math.PI);
        double expectedStiffness = rng.Uniform(Constants.StiffnessMin, Constants.StiffnessMax);

        Assert.Equal(expectedStep, first.BaseX, 12);
        Assert.Equal(expectedHeight, first.Height, 12);
        Assert.Equal(expectedThickness, first.Thickness, 12);
        Assert.Equal((byte)expectedHue, first.Hue);
        Assert.Equal(expectedPhase, first.SwayPhaseOffset, 12);
        Assert.Equal(expectedStiffness, first.Stiffness, 12);
    }

    [Fact]
    public void GenerateIsIdempotentOnSecondCall()
    {
        // The instance Generate path used by MainWindow re-uses a single
        // Sim across (theoretical) regenerations. Calling Generate twice
        // with identical args must yield identical blade arrays.
        var sim = new Sim(Constants.StripHeight + Constants.Headroom);
        sim.Generate(Constants.CanonicalTestSeed, Monitor1920, 1.0);
        var first = sim.Blades.ToArray();
        sim.Generate(Constants.CanonicalTestSeed, Monitor1920, 1.0);
        var second = sim.Blades;

        Assert.Equal(first.Length, second.Length);
        for (int i = 0; i < first.Length; i++)
        {
            Assert.Equal(first[i].BaseX, second[i].BaseX);
            Assert.Equal(first[i].Height, second[i].Height);
            Assert.Equal(first[i].Thickness, second[i].Thickness);
            Assert.Equal(first[i].Hue, second[i].Hue);
            Assert.Equal(first[i].SwayPhaseOffset, second[i].SwayPhaseOffset);
            Assert.Equal(first[i].Stiffness, second[i].Stiffness);
        }
    }
}
