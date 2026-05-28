// StrokeTests.cs — §7 quadratic Bezier stroke geometry.

using DesktopGrass.WinUI3;
using Xunit;

namespace DesktopGrass.WinUI3.Tests.SimTests;

public class StrokeTests
{
    [Fact]
    public void StumpStrokeProducedForVeryCutBlade()
    {
        // CutHeight below CutStumpThreshold (0.05) — render as a tiny
        // straight stump up from the ground.
        var b = new Blade
        {
            BaseX = 100.0,
            Height = 30.0,
            Thickness = 1.5,
            Hue = 2,
            CutHeight = 0.02,
            EffectiveLean = 5.0,
        };
        var stroke = Sim.ComputeBladeStroke(b, groundY: 110.0);
        Assert.Equal(100.0, stroke.BaseX);
        Assert.Equal(110.0, stroke.BaseY);
        Assert.Equal(100.0, stroke.TipX);
        Assert.Equal(110.0 - Constants.StumpHeight, stroke.TipY);
    }

    [Fact]
    public void UncutBladeStrokeUsesEffectiveLean()
    {
        var b = new Blade
        {
            BaseX = 100.0,
            Height = 30.0,
            Thickness = 1.5,
            Hue = 2,
            CutHeight = 1.0,
            EffectiveLean = 5.0,
        };
        var stroke = Sim.ComputeBladeStroke(b, groundY: 110.0);
        Assert.Equal(100.0, stroke.BaseX);
        Assert.Equal(110.0, stroke.BaseY);
        // tipX = baseX + lean = 105; tipY = groundY - height*cutHeight = 80
        Assert.Equal(105.0, stroke.TipX, 9);
        Assert.Equal(80.0, stroke.TipY, 9);
        Assert.Equal(Constants.Palette[2], stroke.Argb);
        Assert.Equal(1.5, stroke.Thickness);
    }

    [Fact]
    public void ZeroLeanStrokeIsVertical()
    {
        var b = new Blade
        {
            BaseX = 200.0,
            Height = 25.0,
            Thickness = 2.0,
            Hue = 0,
            CutHeight = 1.0,
            EffectiveLean = 0.0,
        };
        var stroke = Sim.ComputeBladeStroke(b, groundY: 110.0);
        Assert.Equal(200.0, stroke.BaseX);
        Assert.Equal(110.0, stroke.BaseY);
        Assert.Equal(200.0, stroke.TipX, 9);
        Assert.Equal(85.0, stroke.TipY, 9);
        // No lean → control point is exactly the midpoint between base and
        // tip (offset = CtrlOffsetFactor * 0 = 0).
        Assert.Equal(200.0, stroke.ControlX, 9);
        Assert.Equal((110.0 + 85.0) * 0.5, stroke.ControlY, 9);
    }

    [Fact]
    public void GetStrokeMatchesStaticComputeBladeStroke()
    {
        // The instance Sim.GetStroke(index) and static
        // Sim.ComputeBladeStroke(blade, groundY) must produce identical
        // output, since the renderer uses the instance variant and the
        // test fixture uses the static one.
        var sim = new Sim(Constants.StripHeight + Constants.Headroom);
        sim.Generate(Constants.CanonicalTestSeed, 1920.0, 1.0);
        sim.Tick(1.0 / 60.0, System.ReadOnlySpan<InputEvent>.Empty);

        for (int i = 0; i < System.Math.Min(20, sim.Blades.Length); i++)
        {
            var fromInstance = sim.GetStroke(i);
            var fromStatic = Sim.ComputeBladeStroke(sim.Blades[i], sim.GroundY);

            Assert.Equal(fromInstance.BaseX, fromStatic.BaseX);
            Assert.Equal(fromInstance.BaseY, fromStatic.BaseY);
            Assert.Equal(fromInstance.ControlX, fromStatic.ControlX);
            Assert.Equal(fromInstance.ControlY, fromStatic.ControlY);
            Assert.Equal(fromInstance.TipX, fromStatic.TipX);
            Assert.Equal(fromInstance.TipY, fromStatic.TipY);
            Assert.Equal(fromInstance.Argb, fromStatic.Argb);
            Assert.Equal(fromInstance.Thickness, fromStatic.Thickness);
        }
    }
}
