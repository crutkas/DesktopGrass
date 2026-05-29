// PrngTests.cs - §3 conformance.
// The first 16 uint64 outputs of xorshift64 seeded from CANONICAL_TEST_SEED
// via SplitMix64 are pinned to a fixed snapshot shared across all three
// implementations.

using DesktopGrass.WPF;
using Xunit;

namespace DesktopGrass.WPF.Tests.SimTests;

public class PrngTests
{
    // Computed once and pinned. Any divergence here is a spec violation.
    private static readonly ulong[] CanonicalFirst16 =
    {
        0x3C3A8D4BF44D4757UL,
        0xC5036418082CE819UL,
        0x637C39DC81179789UL,
        0xA8D438AF7ACD7AE6UL,
        0x872C242C0B1C9993UL,
        0xEFA4F8384FDEA460UL,
        0x1C028EE81E340128UL,
        0x292DB46E8579232AUL,
        0xD68F60B495865BECUL,
        0xB92C6D6C0EF02C5BUL,
        0xEA3E31B01AEBBAC3UL,
        0x69414C59CD84BD76UL,
        0x824EF03EDB86298CUL,
        0x2EC0BC0D0F34C6DFUL,
        0x06931E51B1E4F892UL,
        0x51E8736B5F6D55E3UL,
    };

    [Fact]
    public void CanonicalSeedProducesPinnedSequence()
    {
        var p = Prng.Init(Constants.CANONICAL_TEST_SEED);
        for (int i = 0; i < CanonicalFirst16.Length; i++)
        {
            ulong actual = p.NextU64();
            Assert.Equal(CanonicalFirst16[i], actual);
        }
    }

    [Fact]
    public void InitStateNeverZero()
    {
        var p = Prng.Init(0UL);
        Assert.NotEqual(0UL, p.State);
    }

    [Fact]
    public void NextUnitInUnitInterval()
    {
        var p = Prng.Init(Constants.CANONICAL_TEST_SEED);
        for (int i = 0; i < 10_000; i++)
        {
            double v = p.NextUnit();
            Assert.InRange(v, 0.0, 1.0);
            Assert.NotEqual(1.0, v);
        }
    }

    [Theory]
    [InlineData(0.0, 1.0)]
    [InlineData(8.0, 40.0)]
    [InlineData(-5.0, 5.0)]
    public void UniformStaysWithinBounds(double lo, double hi)
    {
        var p = Prng.Init(0xDEADBEEFUL);
        for (int i = 0; i < 1_000; i++)
        {
            double v = p.Uniform(lo, hi);
            Assert.InRange(v, lo, hi);
        }
    }

    [Fact]
    public void IndexProducesValuesInRange()
    {
        var p = Prng.Init(0x1234UL);
        for (int i = 0; i < 1_000; i++)
        {
            uint v = p.Index(6);
            Assert.InRange(v, 0u, 5u);
        }
    }

    [Fact]
    public void DifferentSeedsProduceDifferentSequences()
    {
        var a = Prng.Init(1UL);
        var b = Prng.Init(2UL);
        Assert.NotEqual(a.NextU64(), b.NextU64());
    }
}

