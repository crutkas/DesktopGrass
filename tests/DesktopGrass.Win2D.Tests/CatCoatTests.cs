// CatCoatTests.cs - §17 Cat coat palette and deterministic variant tests.
// Mirrors tests/DesktopGrass.Native.Tests/src/cat_coat_tests.cpp.

using System.Linq;
using DesktopGrass.Win2D;
using Xunit;

namespace DesktopGrass.Win2D.Tests;

public class CatCoatTests
{
    private const double Monitor1920 = 1920.0;

    private static readonly Constants.CatCoatPalette[] ExpectedCatCoats =
    {
        new(0xFF6B6259u, 0xFF3D3733u, 0xFF6B6259u, 0xFF3D3733u, 0xFF1A1614u),
        new(0xFFD89A6Fu, 0xFFA56B40u, 0xFFD89A6Fu, 0xFFA56B40u, 0xFF2B1A0Eu),
        new(0xFF2A2522u, 0xFF140F0Cu, 0xFF2A2522u, 0xFF140F0Cu, 0xFFD9B85Bu),
        new(0xFFEDE9E1u, 0xFFBDB7ABu, 0xFFEDE9E1u, 0xFFBDB7ABu, 0xFF1F1817u),
        new(0xFF7A5F3Cu, 0xFF4E3F26u, 0xFF7A5F3Cu, 0xFF4E3F26u, 0xFF1A1108u),
        new(0xFFC9B898u, 0xFF8E7F6Bu, 0xFFC9B898u, 0xFF8E7F6Bu, 0xFF2E251Du),
    };

    private static Sim BuildSim(ulong seed = Constants.CANONICAL_TEST_SEED,
                                double monitorWidth = Monitor1920,
                                double density = 1.0)
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

    private static int CountKind(Sim sim, EntityKind kind) => sim.Entities.Count(e => e.Kind == kind);

    private static byte NextCatCoatAfterPrefix(ref Prng side)
    {
        double margin = Constants.CAT_BODY_RADIUS + 8.0;
        _ = side.Uniform(margin, Monitor1920 - margin);
        _ = side.Uniform(Constants.CAT_WALK_SPEED_MIN, Constants.CAT_WALK_SPEED_MAX);
        _ = side.Uniform(0.0, 1.0);
        _ = side.NextU32();
        _ = side.Uniform(Constants.CAT_WALK_DURATION_MIN, Constants.CAT_WALK_DURATION_MAX);
        _ = side.Index((uint)Constants.CAT_NAME_POOL.Length);
        return (byte)side.Index((uint)Constants.CAT_COAT_VARIANT_COUNT);
    }

    [Fact]
    public void CatCoatVariantCountIsPinned()
    {
        Assert.Equal(6, Constants.CAT_COAT_VARIANT_COUNT);
    }

    [Fact]
    public void CatCoatPaletteZeroMatchesBackwardCompatibleAliases()
    {
        Assert.Equal(Constants.CAT_BODY_COLOR, Constants.CAT_COAT_PALETTES[0].Body);
        Assert.Equal(Constants.CAT_LEG_COLOR,  Constants.CAT_COAT_PALETTES[0].Leg);
        Assert.Equal(Constants.CAT_FACE_COLOR, Constants.CAT_COAT_PALETTES[0].Face);
        Assert.Equal(Constants.CAT_EAR_COLOR,  Constants.CAT_COAT_PALETTES[0].Ear);
        Assert.Equal(Constants.CAT_INK_COLOR,  Constants.CAT_COAT_PALETTES[0].Ink);
    }

    [Fact]
    public void AllCatCoatPalettesArePinned()
    {
        Assert.Equal(Constants.CAT_COAT_VARIANT_COUNT, Constants.CAT_COAT_PALETTES.Length);
        for (int i = 0; i < Constants.CAT_COAT_VARIANT_COUNT; i++)
        {
            Assert.Equal(ExpectedCatCoats[i].Body, Constants.CAT_COAT_PALETTES[i].Body);
            Assert.Equal(ExpectedCatCoats[i].Leg,  Constants.CAT_COAT_PALETTES[i].Leg);
            Assert.Equal(ExpectedCatCoats[i].Face, Constants.CAT_COAT_PALETTES[i].Face);
            Assert.Equal(ExpectedCatCoats[i].Ear,  Constants.CAT_COAT_PALETTES[i].Ear);
            Assert.Equal(ExpectedCatCoats[i].Ink,  Constants.CAT_COAT_PALETTES[i].Ink);
        }
    }

    [Fact]
    public void CatCoatBodyColorsAreDistinct()
    {
        for (int i = 0; i < Constants.CAT_COAT_VARIANT_COUNT; i++)
        {
            for (int j = i + 1; j < Constants.CAT_COAT_VARIANT_COUNT; j++)
            {
                Assert.NotEqual(Constants.CAT_COAT_PALETTES[i].Body,
                                Constants.CAT_COAT_PALETTES[j].Body);
            }
        }
    }

    [Fact]
    public void CanonicalCatFlockPinsDeterministicCoatVariants()
    {
        var sim = BuildSim();
        sim.SetCritter(CritterKind.Cat);

        byte[] expectedCoats = { 1 };
        var cats = sim.Entities.Where(e => e.Kind == EntityKind.Cat).ToArray();
        Assert.Equal(expectedCoats.Length, cats.Length);
        for (int i = 0; i < cats.Length; i++)
            Assert.Equal(expectedCoats[i], cats[i].CoatVariantIndex);
    }

    [Fact]
    public void CatCoatPrngDrawFollowsNameIndex()
    {
        var side = Prng.Init(Constants.CANONICAL_TEST_SEED ^ Constants.CRITTER_PRNG_SALT);

        var sim = BuildSim();
        sim.SetCritter(CritterKind.Cat);

        double countDraw = side.Uniform(Constants.CAT_COUNT_MIN, Constants.CAT_COUNT_MAX + 1);
        int expectedCount = (int)System.Math.Floor(countDraw);
        if (expectedCount < Constants.CAT_COUNT_MIN) expectedCount = Constants.CAT_COUNT_MIN;
        if (expectedCount > Constants.CAT_COUNT_MAX) expectedCount = Constants.CAT_COUNT_MAX;
        Assert.Equal(expectedCount, CountKind(sim, EntityKind.Cat));

        int seen = 0;
        foreach (var e in sim.Entities)
        {
            if (e.Kind != EntityKind.Cat) continue;
            byte expectedCoat = NextCatCoatAfterPrefix(ref side);
            Assert.Equal(expectedCoat, e.CoatVariantIndex);
            seen++;
        }
        Assert.Equal(expectedCount, seen);
    }

    [Fact]
    public void GeneratedCatCoatsAlwaysStayWithinPaletteRange()
    {
        for (ulong i = 0; i < 128; i++)
        {
            ulong seed = unchecked(Constants.CANONICAL_TEST_SEED + i * 0x9E3779B97F4A7C15UL);
            var sim = BuildSim(seed);
            sim.SetCritter(CritterKind.Cat);

            var cats = sim.Entities.Where(e => e.Kind == EntityKind.Cat).ToArray();
            Assert.InRange(cats.Length, Constants.CAT_COUNT_MIN, Constants.CAT_COUNT_MAX);
            foreach (var cat in cats)
                Assert.True(cat.CoatVariantIndex < Constants.CAT_COAT_VARIANT_COUNT);
        }
    }

    [Fact]
    public void SheepKeepDefaultCoatVariantZero()
    {
        var sim = BuildSim();
        sim.SetCritter(CritterKind.Sheep);

        var sheep = sim.Entities.Where(e => e.Kind == EntityKind.Sheep).ToArray();
        Assert.True(sheep.Length >= Constants.SHEEP_COUNT_MIN);
        foreach (var e in sheep)
            Assert.Equal((byte)0, e.CoatVariantIndex);
    }

    [Fact]
    public void FixedCatCountCoatPrngSkipsOnlyTheCountDraw()
    {
        var sim = BuildSim();
        sim.SetCritter(CritterKind.Cat);
        sim.SetCritterCount(3);
        Assert.Equal(3, CountKind(sim, EntityKind.Cat));

        var side = Prng.Init(Constants.CANONICAL_TEST_SEED ^ Constants.CRITTER_PRNG_SALT);

        int seen = 0;
        foreach (var e in sim.Entities)
        {
            if (e.Kind != EntityKind.Cat) continue;
            byte expectedCoat = NextCatCoatAfterPrefix(ref side);
            Assert.Equal(expectedCoat, e.CoatVariantIndex);
            seen++;
        }
        Assert.Equal(3, seen);
    }
}
