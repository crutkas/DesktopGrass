// SceneTests.cs - §13 scene infrastructure.

using DesktopGrass.Win2D;
using Xunit;

namespace DesktopGrass.Win2D.Tests.SimTests;

public class SceneTests
{
    private const double Monitor1920 = 1920.0;

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
        return sim;
    }

    [Fact]
    public void SceneEnumHasSpecLockedDiscriminants()
    {
        Assert.Equal(0, (int)Scene.Grass);
        Assert.Equal(1, (int)Scene.Desert);
        Assert.Equal(2, (int)Scene.Winter);
        Assert.Equal(3, Constants.SCENE_COUNT);
        Assert.Equal(0, (int)Constants.SCENE_DEFAULT);
    }

    [Fact]
    public void FreshSimDefaultsCurrentSceneToGrass()
    {
        var sim = new Sim();
        Assert.Equal(Scene.Grass, sim.CurrentScene);
    }

    [Fact]
    public void SetSceneIsStateOnlyAndDoesNotPerturbBlades()
    {
        var a = BuildSim();
        var b = BuildSim();

        Assert.Equal(a.Blades.Length, b.Blades.Length);
        b.SetScene(Scene.Desert);

        Assert.Equal(Scene.Desert, b.CurrentScene);
        Assert.Equal(Scene.Grass, a.CurrentScene);
        Assert.Equal(a.Blades.Length, b.Blades.Length);
        for (int i = 0; i < a.Blades.Length; i++)
        {
            Assert.Equal(a.Blades[i].BaseX, b.Blades[i].BaseX);
            Assert.Equal(a.Blades[i].Height, b.Blades[i].Height);
            Assert.Equal(a.Blades[i].Thickness, b.Blades[i].Thickness);
            Assert.Equal(a.Blades[i].Hue, b.Blades[i].Hue);
            Assert.Equal(a.Blades[i].IsFlower, b.Blades[i].IsFlower);
            Assert.Equal(a.Blades[i].IsMushroom, b.Blades[i].IsMushroom);
        }
        Assert.Equal(a.AmbientPrng.State, b.AmbientPrng.State);
        Assert.Equal(a.NextAmbientGustTime, b.NextAmbientGustTime);
    }

    [Fact]
    public void SetSceneRoundTripsThroughAllValues()
    {
        var sim = BuildSim(density: Constants.DEFAULT_DENSITY);
        Assert.Equal(Scene.Grass, sim.CurrentScene);
        sim.SetScene(Scene.Desert);
        Assert.Equal(Scene.Desert, sim.CurrentScene);
        sim.SetScene(Scene.Winter);
        Assert.Equal(Scene.Winter, sim.CurrentScene);
        sim.SetScene(Scene.Grass);
        Assert.Equal(Scene.Grass, sim.CurrentScene);
    }

    [Fact]
    public void PerScenePaletteTablesAreFullAlphaArgbEntries()
    {
        for (int s = 0; s < Constants.SCENE_COUNT; s++)
        {
            for (int i = 0; i < Constants.PALETTE_SIZE; i++)
            {
                uint argb = Constants.SCENE_PALETTES[s, i];
                byte alpha = (byte)((argb >> 24) & 0xFFu);
                Assert.Equal((byte)0xFF, alpha);
            }
        }
    }

    [Fact]
    public void GrassScenePaletteIsBitIdenticalToOriginalPalette()
    {
        for (int i = 0; i < Constants.PALETTE_SIZE; i++)
        {
            Assert.Equal(Constants.PALETTE[i], Constants.SCENE_PALETTES[(int)Scene.Grass, i]);
        }
    }

    [Fact]
    public void DesertPaletteValuesMatchSpec()
    {
        uint[] expected =
        {
            0xFFC9A26B, 0xFFB48A56, 0xFFD9B57A,
            0xFF8F6E3F, 0xFFE6C896, 0xFFA67843,
        };

        for (int i = 0; i < Constants.PALETTE_SIZE; i++)
        {
            Assert.Equal(expected[i], Constants.SCENE_PALETTES[(int)Scene.Desert, i]);
            Assert.Equal(expected[i], Constants.DESERT_PALETTE[i]);
        }
    }

    [Fact]
    public void WinterPaletteValuesMatchSpec()
    {
        uint[] expected =
        {
            0xFFE8EEF5, 0xFFB7C4D2, 0xFFCBD8E5,
            0xFFD7E2EE, 0xFFA8B7C6, 0xFFEEF3F8,
        };

        for (int i = 0; i < Constants.PALETTE_SIZE; i++)
        {
            Assert.Equal(expected[i], Constants.SCENE_PALETTES[(int)Scene.Winter, i]);
            Assert.Equal(expected[i], Constants.WINTER_PALETTE[i]);
        }
    }
}
