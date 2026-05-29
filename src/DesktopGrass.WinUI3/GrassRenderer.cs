// GrassRenderer.cs
//
// Composition-based renderer. Builds one ShapeVisual per blade with a
// CompositionPathGeometry whose path is rewritten every frame to the
// current quadratic Bezier (see Sim.GetStroke).
//
// Friction note for the comparison doc: WinUI 3's Microsoft.UI.Composition
// has ShapeVisual + CompositionPathGeometry, but no native path builder.
// CompositionPath takes an IGeometrySource2D, and the only practical
// implementation that ships is Win2D's CanvasGeometry. So even the
// "pure WinUI 3" renderer transitively depends on Win2D (or on bringing
// your own IGeometrySource2D, which is materially worse). The Native
// (Direct2D) and Win2D tracks have no such dependency chain.

using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Windows.UI;

namespace DesktopGrass.WinUI3;

internal sealed class GrassRenderer
{
    private readonly Sim _sim;
    private readonly FrameworkElement _host;
    private readonly Compositor _compositor;
    private readonly ShapeVisual _root;
    private readonly CompositionColorBrush[] _brushes;
    private readonly CompositionColorBrush[] _flowerHeadBrushes;
    private readonly CompositionSpriteShape[] _shapes;
    private readonly CompositionPathGeometry[] _paths;
    private readonly CompositionEllipseGeometry?[] _flowerHeadGeometries;
    private readonly CanvasDevice _device;

    public GrassRenderer(FrameworkElement host, Sim sim)
    {
        _sim = sim;
        _host = host;

        _compositor = ElementCompositionPreview.GetElementVisual(host).Compositor;
        _device = CanvasDevice.GetSharedDevice();

        _root = _compositor.CreateShapeVisual();
        // Initial size — gets resized in Render() to match the host element.
        _root.Size = new Vector2(
            (float)Constants.StripHeight * 24f,
            (float)Constants.StripHeight + (float)Constants.Headroom);

        ElementCompositionPreview.SetElementChildVisual(host, _root);

        // Pre-create one stroke brush per palette colour.
        _brushes = new CompositionColorBrush[Constants.Palette.Length];
        for (int i = 0; i < _brushes.Length; i++)
        {
            uint argb = Constants.Palette[i];
            var color = Color.FromArgb(
                (byte)((argb >> 24) & 0xFF),
                (byte)((argb >> 16) & 0xFF),
                (byte)((argb >> 8) & 0xFF),
                (byte)(argb & 0xFF));
            _brushes[i] = _compositor.CreateColorBrush(color);
        }

        _flowerHeadBrushes = new CompositionColorBrush[Constants.FlowerPalette.Length];
        for (int i = 0; i < _flowerHeadBrushes.Length; i++)
        {
            uint argb = Constants.FlowerPalette[i];
            var color = Color.FromArgb(
                (byte)((argb >> 24) & 0xFF),
                (byte)((argb >> 16) & 0xFF),
                (byte)((argb >> 8) & 0xFF),
                (byte)(argb & 0xFF));
            _flowerHeadBrushes[i] = _compositor.CreateColorBrush(color);
        }

        // One shape + path per blade, allocated up-front.
        int n = _sim.Blades.Length;
        _shapes = new CompositionSpriteShape[n];
        _paths  = new CompositionPathGeometry[n];
        _flowerHeadGeometries = new CompositionEllipseGeometry?[n];

        for (int i = 0; i < n; i++)
        {
            ref readonly var b = ref _sim.Blades[i];

            var pathGeom = _compositor.CreatePathGeometry();
            _paths[i] = pathGeom;

            var shape = _compositor.CreateSpriteShape(pathGeom);
            byte hue = b.Hue;
            shape.StrokeBrush = _brushes[hue];
            shape.StrokeThickness = (float)b.Thickness;
            shape.StrokeStartCap = CompositionStrokeCap.Round;
            shape.StrokeEndCap = CompositionStrokeCap.Round;

            _shapes[i] = shape;
            _root.Shapes.Add(shape);

            if (b.IsFlower)
            {
                int hi = b.FlowerHeadColorIdx;
                if ((uint)hi >= (uint)_flowerHeadBrushes.Length) hi = 0;

                var headGeom = _compositor.CreateEllipseGeometry();
                headGeom.Radius = Vector2.Zero;
                _flowerHeadGeometries[i] = headGeom;

                var headShape = _compositor.CreateSpriteShape(headGeom);
                headShape.FillBrush = _flowerHeadBrushes[hi];
                _root.Shapes.Add(headShape);
            }
        }
    }

    public void Render()
    {
        // Resize the root visual to the current host bounds. ActualWidth is
        // 0 before the first measure pass on some WinUI 3 builds — fall
        // back to the configured strip width.
        float w = _host.ActualWidth > 0 ? (float)_host.ActualWidth : _root.Size.X;
        float h = (float)(Constants.StripHeight + Constants.Headroom);
        if (_root.Size.X != w || _root.Size.Y != h)
        {
            _root.Size = new Vector2(w, h);
        }

        int n = _sim.Blades.Length;
        for (int i = 0; i < n; i++)
        {
            ref readonly var b = ref _sim.Blades[i];
            var s = _sim.GetStroke(i);

            // Build a fresh quadratic Bezier path via Win2D's CanvasPathBuilder
            // (the only practical IGeometrySource2D implementation that ships).
            CanvasGeometry geom;
            using (var pb = new CanvasPathBuilder(_device))
            {
                pb.BeginFigure((float)s.BaseX, (float)s.BaseY, CanvasFigureFill.DoesNotAffectFills);
                pb.AddQuadraticBezier(
                    new Vector2((float)s.ControlX, (float)s.ControlY),
                    new Vector2((float)s.TipX,     (float)s.TipY));
                pb.EndFigure(CanvasFigureLoop.Open);
                geom = CanvasGeometry.CreatePath(pb);
            }

            _paths[i].Path = new CompositionPath(geom);

            var shape = _shapes[i];
            if (shape.StrokeThickness != (float)s.Thickness)
            {
                shape.StrokeThickness = (float)s.Thickness;
            }

            var headGeom = _flowerHeadGeometries[i];
            if (headGeom is not null)
            {
                if (b.IsFlower && b.CutHeight >= Constants.CutStumpThreshold)
                {
                    float radius = (float)b.FlowerHeadRadius;
                    headGeom.Center = new Vector2((float)s.TipX, (float)s.TipY);
                    headGeom.Radius = new Vector2(radius, radius);
                }
                else
                {
                    headGeom.Radius = Vector2.Zero;
                }
            }
        }
    }
}
