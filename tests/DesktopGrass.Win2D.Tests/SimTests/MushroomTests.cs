// MushroomTests.cs - §5 mushroom stream + non-interference with main/regrowth/flower.

using System;
using System.Linq;
using DesktopGrass.Win2D;
using Xunit;

namespace DesktopGrass.Win2D.Tests.SimTests;

public class MushroomTests
{
    private const double Monitor1920 = 1920.0;

    [Fact]
    public void MushroomStreamIsDeterministic()
    {
        var a = Sim.GenerateBlades(Constants.CANONICAL_TEST_SEED, Monitor1920, 1.0);
        var b = Sim.GenerateBlades(Constants.CANONICAL_TEST_SEED, Monitor1920, 1.0);

        Assert.Equal(a.Length, b.Length);
        for (int i = 0; i < a.Length; i++)
        {
            Assert.Equal(a[i].IsMushroom, b[i].IsMushroom);
            Assert.Equal(a[i].MushroomCapColorIdx, b[i].MushroomCapColorIdx);
            Assert.Equal(a[i].MushroomCapWidth, b[i].MushroomCapWidth);
            Assert.Equal(a[i].MushroomCapHeight, b[i].MushroomCapHeight);
            Assert.Equal(a[i].MushroomStemHeight, b[i].MushroomStemHeight);
            Assert.Equal(a[i].MushroomStemThickness, b[i].MushroomStemThickness);
        }
    }

    [Fact]
    public void MushroomCountIsWithin3SigmaOfProbability()
    {
        var blades = Sim.GenerateBlades(Constants.CANONICAL_TEST_SEED, Monitor1920, 1.0);
        Assert.True(blades.Length > 100);

        int count = blades.Count(b => b.IsMushroom);
        double n  = blades.Length;
        double p  = Constants.MUSHROOM_PROBABILITY;
        double mu = n * p;
        double sd = Math.Sqrt(n * p * (1.0 - p));

        Assert.InRange(count, (int)Math.Floor(mu - 3.0 * sd), (int)Math.Ceiling(mu + 3.0 * sd));
    }

    [Fact]
    public void MushroomStreamDoesNotPerturbMainStream()
    {
        var blades = Sim.GenerateBlades(Constants.CANONICAL_TEST_SEED, Monitor1920, 1.0);
        Assert.True(blades.Length > 0);
        Assert.Equal(4.941073726820111, blades[0].BaseX, 12);
        Assert.Equal(24.469991818248864, blades[0].Height, 12);
        Assert.Equal(1.5829214329729786, blades[0].Thickness, 12);
        Assert.Equal((byte)3, blades[0].Hue);
    }

    [Fact]
    public void NonMushroomBladesHaveZeroMushroomFields()
    {
        var blades = Sim.GenerateBlades(Constants.CANONICAL_TEST_SEED, Monitor1920, 1.0);
        foreach (ref readonly var b in blades.AsSpan())
        {
            if (!b.IsMushroom)
            {
                Assert.Equal((byte)0, b.MushroomCapColorIdx);
                Assert.Equal(0.0, b.MushroomCapWidth);
                Assert.Equal(0.0, b.MushroomCapHeight);
                Assert.Equal(0.0, b.MushroomStemHeight);
                Assert.Equal(0.0, b.MushroomStemThickness);
            }
        }
    }
}
