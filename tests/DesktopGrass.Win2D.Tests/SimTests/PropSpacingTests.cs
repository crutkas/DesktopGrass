// PropSpacingTests.cs - §15.6 Minimum spacing between adjacent props.

using System;
using System.Collections.Generic;
using DesktopGrass.Win2D;
using Xunit;

namespace DesktopGrass.Win2D.Tests.SimTests;

public class PropSpacingTests
{
    private const double Monitor1920 = 1920.0;

    private readonly record struct Prop(double LeftEdge, double RightEdge);

    private static Sim BuildSim()
    {
        var sim = new Sim
        {
            Blades = Sim.GenerateBlades(Constants.CANONICAL_TEST_SEED, Monitor1920, Constants.DEFAULT_DENSITY),
            WindowHeight = Constants.STRIP_HEIGHT + Constants.HEADROOM,
            GroundY = Constants.STRIP_HEIGHT + Constants.HEADROOM,
        };
        sim.ResetAmbientGusts(Constants.CANONICAL_TEST_SEED, Monitor1920);
        sim.ResetEntities(Constants.CANONICAL_TEST_SEED);
        return sim;
    }

    private static double CactusHalfWidth(in Blade b)
        => (b.CactusType != 0) ? b.CactusWidth * 1.55 : b.CactusWidth * 0.5;

    private static double PineHalfWidth(in Blade b)
    {
        double hw = (b.TreeVariant == 1) ? b.PineWidth * 4.0 : b.PineWidth * 0.5;
        if (b.TreeBackground) hw *= Constants.TREE_BG_SCALE;
        return hw;
    }

    // Walk a kind+layer's props left-to-right and assert the right edge of
    // each is at least PROP_MIN_GAP_DIP behind the left edge of the next.
    // Generators emit props in BaseX order so a linear pass suffices.
    private static void RequireSpacing(IReadOnlyList<Prop> props, string label)
    {
        Assert.True(props.Count >= 1, $"{label}: expected at least one placement");
        for (int i = 1; i < props.Count; i++)
        {
            double gap = props[i].LeftEdge - props[i - 1].RightEdge;
            Assert.True(gap >= Constants.PROP_MIN_GAP_DIP,
                $"{label}: pair {i - 1}->{i} gap={gap:F3} < min {Constants.PROP_MIN_GAP_DIP:F3}");
        }
    }

    [Fact]
    public void DesertCactiKeepMinGapBetweenNeighbours()
    {
        var sim = BuildSim();
        sim.SetScene(Scene.Desert);

        var cacti = new List<Prop>();
        foreach (var b in sim.Blades)
        {
            if (!b.IsCactus) continue;
            double hw = CactusHalfWidth(b);
            cacti.Add(new Prop(b.BaseX - hw, b.BaseX + hw));
        }
        RequireSpacing(cacti, "cacti");
    }

    [Fact]
    public void WinterPinesKeepMinGapWithinSameLayer()
    {
        var sim = BuildSim();
        sim.SetScene(Scene.Winter);

        var fg = new List<Prop>();
        var bg = new List<Prop>();
        foreach (var b in sim.Blades)
        {
            if (!b.IsPine) continue;
            double hw = PineHalfWidth(b);
            var p = new Prop(b.BaseX - hw, b.BaseX + hw);
            if (b.TreeBackground) bg.Add(p);
            else                  fg.Add(p);
        }
        RequireSpacing(fg, "foreground pines");
        RequireSpacing(bg, "background pines");
    }

    [Fact]
    public void AutumnMaplesKeepMinGapBetweenNeighbours()
    {
        var sim = BuildSim();
        sim.SetScene(Scene.Autumn);

        var maples = new List<Prop>();
        foreach (var b in sim.Blades)
        {
            if (!b.IsMaple) continue;
            double hw = b.MapleCanopyRadius;
            maples.Add(new Prop(b.BaseX - hw, b.BaseX + hw));
        }
        RequireSpacing(maples, "maples");
    }

    [Fact]
    public void OceanCoralKeepMinGapBetweenNeighbours()
    {
        var sim = BuildSim();
        sim.SetScene(Scene.Ocean);

        var coral = new List<Prop>();
        foreach (var b in sim.Blades)
        {
            if (!b.IsCoral) continue;
            double hw = b.CoralWidth * 0.5;
            coral.Add(new Prop(b.BaseX - hw, b.BaseX + hw));
        }
        RequireSpacing(coral, "coral");
    }

    [Fact]
    public void SpacingRuleStillLeavesEachSceneWithAFewProps()
    {
        var sim = BuildSim();

        sim.SetScene(Scene.Desert);
        int cactusCount = 0;
        foreach (var b in sim.Blades) if (b.IsCactus) cactusCount++;
        Assert.True(cactusCount >= 1, $"Desert cactus count after gap rule: {cactusCount}");

        sim.SetScene(Scene.Winter);
        int pineCount = 0;
        foreach (var b in sim.Blades) if (b.IsPine) pineCount++;
        Assert.True(pineCount >= 3, $"Winter pine count after gap rule: {pineCount}");

        sim.SetScene(Scene.Autumn);
        int mapleCount = 0;
        foreach (var b in sim.Blades) if (b.IsMaple) mapleCount++;
        Assert.True(mapleCount >= 1, $"Autumn maple count after gap rule: {mapleCount}");

        sim.SetScene(Scene.Ocean);
        int coralCount = 0;
        foreach (var b in sim.Blades) if (b.IsCoral) coralCount++;
        Assert.True(coralCount >= 5, $"Ocean coral count after gap rule: {coralCount}");
    }
}
