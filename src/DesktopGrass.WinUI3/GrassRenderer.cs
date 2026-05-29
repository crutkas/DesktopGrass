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
    private readonly CompositionColorBrush[] _mushroomCapBrushes;
    private readonly CompositionColorBrush _mushroomStemBrush;
    private readonly CompositionSpriteShape[] _shapes;
    private readonly CompositionPathGeometry[] _paths;
    private readonly CompositionEllipseGeometry?[] _flowerHeadGeometries;
    private readonly CompositionEllipseGeometry?[] _mushroomCapGeometries;
    private readonly CompositionLineGeometry?[] _mushroomStemGeometries;
    private readonly CompositionSpriteShape?[] _mushroomStemShapes;
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

        _mushroomCapBrushes = new CompositionColorBrush[Constants.MushroomPalette.Length];
        for (int i = 0; i < _mushroomCapBrushes.Length; i++)
        {
            uint argb = Constants.MushroomPalette[i];
            var color = Color.FromArgb(
                (byte)((argb >> 24) & 0xFF),
                (byte)((argb >> 16) & 0xFF),
                (byte)((argb >> 8) & 0xFF),
                (byte)(argb & 0xFF));
            _mushroomCapBrushes[i] = _compositor.CreateColorBrush(color);
        }
        {
            uint argb = Constants.MushroomStemColor;
            var stemColor = Color.FromArgb(
                (byte)((argb >> 24) & 0xFF),
                (byte)((argb >> 16) & 0xFF),
                (byte)((argb >> 8) & 0xFF),
                (byte)(argb & 0xFF));
            _mushroomStemBrush = _compositor.CreateColorBrush(stemColor);
        }

        // One shape + path per blade, allocated up-front.
        int n = _sim.Blades.Length;
        _shapes = new CompositionSpriteShape[n];
        _paths  = new CompositionPathGeometry[n];
        _flowerHeadGeometries = new CompositionEllipseGeometry?[n];
        _mushroomCapGeometries  = new CompositionEllipseGeometry?[n];
        _mushroomStemGeometries = new CompositionLineGeometry?[n];
        _mushroomStemShapes     = new CompositionSpriteShape?[n];

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

            if (b.IsMushroom)
            {
                int ci = b.MushroomCapColorIdx;
                if ((uint)ci >= (uint)_mushroomCapBrushes.Length) ci = 0;

                var stemGeom = _compositor.CreateLineGeometry();
                stemGeom.Start = Vector2.Zero;
                stemGeom.End   = Vector2.Zero;
                _mushroomStemGeometries[i] = stemGeom;

                var stemShape = _compositor.CreateSpriteShape(stemGeom);
                stemShape.StrokeBrush     = _mushroomStemBrush;
                stemShape.StrokeThickness = (float)b.MushroomStemThickness;
                _mushroomStemShapes[i]    = stemShape;
                _root.Shapes.Add(stemShape);

                var capGeom = _compositor.CreateEllipseGeometry();
                capGeom.Center = Vector2.Zero;
                capGeom.Radius = Vector2.Zero;
                _mushroomCapGeometries[i] = capGeom;

                var capShape = _compositor.CreateSpriteShape(capGeom);
                capShape.FillBrush = _mushroomCapBrushes[ci];
                _root.Shapes.Add(capShape);
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

            if (b.IsMushroom)
            {
                var capGeom  = _mushroomCapGeometries[i];
                var stemGeom = _mushroomStemGeometries[i];
                if (capGeom is null || stemGeom is null) continue;

                float baseX = (float)b.BaseX;
                float gy    = (float)_sim.GroundY;

                if (b.CutHeight < Constants.CutStumpThreshold)
                {
                    stemGeom.Start = new Vector2(baseX, gy);
                    stemGeom.End   = new Vector2(baseX, gy - (float)Constants.MushroomStumpHeight);
                    capGeom.Radius = Vector2.Zero;
                }
                else
                {
                    float scale = (float)b.CutHeight;
                    float stemH = (float)b.MushroomStemHeight * scale;
                    float capRX = (float)b.MushroomCapWidth   * scale;
                    float capRY = (float)b.MushroomCapHeight  * scale;
                    float capCY = gy - stemH;

                    stemGeom.Start = new Vector2(baseX, gy);
                    stemGeom.End   = new Vector2(baseX, capCY);
                    capGeom.Center = new Vector2(baseX, capCY);
                    capGeom.Radius = new Vector2(capRX, capRY);
                }

                var stemShape = _mushroomStemShapes[i];
                if (stemShape is not null)
                {
                    stemShape.StrokeThickness = (float)b.MushroomStemThickness;
                }

                var bladeShape = _shapes[i];
                bladeShape.StrokeThickness = 0f;

                var flowerHead = _flowerHeadGeometries[i];
                if (flowerHead is not null)
                {
                    flowerHead.Radius = Vector2.Zero;
                }
                continue;
            }

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
