// FlowerTests.cs - §5 flower stream + non-interference with the main stream.

using System;
using System.Linq;
using DesktopGrass.WPF;
using Xunit;

namespace DesktopGrass.WPF.Tests.SimTests;

public class FlowerTests
{
    private const double Monitor1920 = 1920.0;

    [Fact]
    public void FlowerStreamIsDeterministic()
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
    public void FlowerCountIsWithin3SigmaOfProbability()
    {
        var blades = Sim.GenerateBlades(Constants.CANONICAL_TEST_SEED, Monitor1920, 1.0);
        Assert.True(blades.Length > 100);
        int flowerCount = blades.Count(b => b.IsFlower);
        double n  = blades.Length;
        double p  = Constants.FLOWER_PROBABILITY;
        double mu = n * p;
        double sd = Math.Sqrt(n * p * (1.0 - p));
        Assert.InRange(flowerCount, (int)Math.Floor(mu - 3.0 * sd), (int)Math.Ceiling(mu + 3.0 * sd));
    }

    [Fact]
    public void FlowerStreamDoesNotPerturbMainStream()
    {
        var blades = Sim.GenerateBlades(Constants.CANONICAL_TEST_SEED, Monitor1920, 1.0);
        Assert.True(blades.Length > 0);
        Assert.Equal(4.941073726820111, blades[0].BaseX, 12);
        Assert.Equal(24.469991818248864, blades[0].Height, 12);
        Assert.Equal(1.5829214329729786, blades[0].Thickness, 12);
        Assert.Equal((byte)3, blades[0].Hue);
    }

    [Fact]
    public void NonFlowerBladesHaveUnitHeightBonus()
    {
        var blades = Sim.GenerateBlades(Constants.CANONICAL_TEST_SEED, Monitor1920, 1.0);
        foreach (ref readonly var b in blades.AsSpan())
        {
            if (!b.IsFlower)
            {
                Assert.Equal(1.0, b.HeightBonus);
                Assert.Equal(0.0, b.FlowerHeadRadius);
            }
        }
    }
}
