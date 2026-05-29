using System;
using System.Windows;
using System.Windows.Media;
using MediaBrush = System.Windows.Media.Brush;
using MediaColor = System.Windows.Media.Color;
using MediaPen = System.Windows.Media.Pen;
using WpfPoint = System.Windows.Point;

namespace DesktopGrass.WPF;

internal sealed class GrassCanvas : FrameworkElement
{
    private readonly Sim _sim;
    private readonly MediaBrush[] _brushes;
    private readonly MediaBrush[] _flowerHeadBrushes;

    public GrassCanvas(Sim sim)
    {
        _sim = sim;
        IsHitTestVisible = false;
        ClipToBounds = false;
        _brushes = new MediaBrush[Constants.PALETTE.Length];
        for (int i = 0; i < Constants.PALETTE.Length; i++)
        {
            uint argb = Constants.PALETTE[i];
            var brush = new SolidColorBrush(MediaColor.FromArgb(
                (byte)(argb >> 24),
                (byte)(argb >> 16),
                (byte)(argb >> 8),
                (byte)argb));
            brush.Freeze();
            _brushes[i] = brush;
        }

        _flowerHeadBrushes = new MediaBrush[Constants.FLOWER_PALETTE.Length];
        for (int i = 0; i < Constants.FLOWER_PALETTE.Length; i++)
        {
            uint argb = Constants.FLOWER_PALETTE[i];
            var brush = new SolidColorBrush(MediaColor.FromArgb(
                (byte)(argb >> 24),
                (byte)(argb >> 16),
                (byte)(argb >> 8),
                (byte)argb));
            brush.Freeze();
            _flowerHeadBrushes[i] = brush;
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        double groundY = _sim.GroundY;
        foreach (ref readonly Blade blade in _sim.Blades.AsSpan())
        {
            var stroke = Sim.ComputeBladeStroke(in blade, groundY);
            var geometry = new StreamGeometry();
            using (StreamGeometryContext context = geometry.Open())
            {
                context.BeginFigure(
                    new WpfPoint(stroke.BaseX, stroke.BaseY),
                    isFilled: false,
                    isClosed: false);
                context.QuadraticBezierTo(
                    new WpfPoint(stroke.CtrlX, stroke.CtrlY),
                    new WpfPoint(stroke.TipX, stroke.TipY),
                    isStroked: true,
                    isSmoothJoin: false);
            }
            geometry.Freeze();

            int paletteIndex = Math.Clamp(blade.Hue, 0, _brushes.Length - 1);
            var pen = new MediaPen(_brushes[paletteIndex], stroke.Thickness)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
                LineJoin = PenLineJoin.Round,
            };
            pen.Freeze();
            dc.DrawGeometry(null, pen, geometry);

            if (blade.IsFlower && blade.CutHeight >= Constants.CUT_STUMP_THRESHOLD)
            {
                int hi = blade.FlowerHeadColorIdx;
                if ((uint)hi >= (uint)_flowerHeadBrushes.Length) hi = 0;
                dc.DrawEllipse(
                    _flowerHeadBrushes[hi],
                    null,
                    new WpfPoint(stroke.TipX, stroke.TipY),
                    blade.FlowerHeadRadius,
                    blade.FlowerHeadRadius);
            }
        }
    }
}
