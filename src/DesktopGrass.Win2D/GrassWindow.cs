// GrassWindow.cs - one HWND per monitor, hosting a DComp visual + DXGI
// composition-mode swap chain + a Direct2D device context.
//
// Coordinate model: blade math is in DIPs (Sim works in window-local DIPs);
// the D2D target bitmap is configured with DPI = 96 * scale so DIP-space
// draw calls produce crisp pixel output on hi-DPI monitors.
//
// Lifecycle: created on the main thread; CreateGraphics builds the entire
// graphics chain. Dispose is best-effort and tolerates partial setup.

using System;
using System.Drawing;
using System.Numerics;
using DesktopGrass.Win2D.Interop;
using Vortice.DCommon;
using Vortice.Direct2D1;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DirectComposition;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace DesktopGrass.Win2D;

internal sealed class GrassWindow : IDisposable
{
    private readonly IntPtr _hwnd;
    private readonly int _widthPx;
    private readonly int _heightPx;
    private readonly float _dpiScale;
    private readonly Rectangle _monitorBoundsPx;

    private ID3D11Device? _d3dDevice;
    private IDXGIDevice? _dxgiDevice;
    private IDXGIFactory2? _dxgiFactory;
    private IDXGISwapChain1? _swapChain;
    private ID2D1Factory1? _d2dFactory;
    private ID2D1Device? _d2dDevice;
    private ID2D1DeviceContext? _dc;
    private ID2D1Bitmap1? _targetBitmap;
    private IDCompositionDesktopDevice? _dcompDevice;
    private IDCompositionTarget? _dcompTarget;
    private IDCompositionVisual2? _dcompVisual;
    private ID2D1SolidColorBrush[,]? _brushes; // [SCENE_COUNT, PALETTE_SIZE]
    private ID2D1SolidColorBrush[]? _flowerHeadBrushes;
    private ID2D1SolidColorBrush[]? _mushroomCapBrushes;
    private ID2D1SolidColorBrush? _mushroomStemBrush;
    private ID2D1SolidColorBrush? _cactusBrush;
    private ID2D1SolidColorBrush? _tumbleweedBrush;
    private ID2D1SolidColorBrush? _snowflakeBrush;
    private ID2D1SolidColorBrush? _snowTipBrush;
    private ID2D1StrokeStyle? _strokeStyle;

    public Sim Sim { get; }
    public IntPtr Hwnd => _hwnd;
    public int WidthPx => _widthPx;
    public int HeightPx => _heightPx;
    public float DpiScale => _dpiScale;
    public Rectangle MonitorBoundsPx => _monitorBoundsPx;

    public void SetScene(Scene s) => Sim.SetScene(s);

    public GrassWindow(IntPtr hwnd, int widthPx, int heightPx, float dpiScale,
                       Rectangle monitorBoundsPx, ulong seed, double monitorWidthDip)
    {
        _hwnd = hwnd;
        _widthPx = widthPx;
        _heightPx = heightPx;
        _dpiScale = dpiScale;
        _monitorBoundsPx = monitorBoundsPx;

        Sim = new Sim
        {
            Blades = Sim.GenerateBlades(seed, monitorWidthDip, Constants.DEFAULT_DENSITY),
            GroundY = _heightPx / _dpiScale,
            WindowHeight = _heightPx / _dpiScale,
        };
        Sim.ResetAmbientGusts(seed, monitorWidthDip);
        Sim.ResetEntities(seed);

        CreateGraphics();
    }

    private void CreateGraphics()
    {
        // ----- D3D11 -----
        var flags = DeviceCreationFlags.BgraSupport;
        var featureLevels = new[]
        {
            Vortice.Direct3D.FeatureLevel.Level_11_1,
            Vortice.Direct3D.FeatureLevel.Level_11_0,
            Vortice.Direct3D.FeatureLevel.Level_10_1,
            Vortice.Direct3D.FeatureLevel.Level_10_0,
        };
        var result = D3D11.D3D11CreateDevice(
            adapter: (IDXGIAdapter?)null,
            DriverType.Hardware,
            flags,
            featureLevels,
            out _d3dDevice);

        if (result.Failure || _d3dDevice is null)
        {
            // Fallback to WARP if hardware fails (RDP, no GPU, etc.).
            D3D11.D3D11CreateDevice(
                adapter: (IDXGIAdapter?)null,
                DriverType.Warp,
                flags,
                featureLevels,
                out _d3dDevice).CheckError();
        }

        _dxgiDevice = _d3dDevice!.QueryInterface<IDXGIDevice>();

        // ----- DXGI factory + composition swap chain -----
        _dxgiFactory = DXGI.CreateDXGIFactory2<IDXGIFactory2>(false);

        var swapDesc = new SwapChainDescription1
        {
            Width = (uint)_widthPx,
            Height = (uint)_heightPx,
            Format = Format.B8G8R8A8_UNorm,
            Stereo = false,
            SampleDescription = new SampleDescription(1, 0),
            BufferUsage = Usage.RenderTargetOutput,
            BufferCount = 2,
            Scaling = Scaling.Stretch,
            SwapEffect = SwapEffect.FlipSequential,
            AlphaMode = Vortice.DXGI.AlphaMode.Premultiplied,
            Flags = SwapChainFlags.None,
        };
        _swapChain = _dxgiFactory.CreateSwapChainForComposition(_d3dDevice, swapDesc);

        // ----- Direct2D device + context -----
        _d2dFactory = D2D1.D2D1CreateFactory<ID2D1Factory1>(FactoryType.SingleThreaded);
        _d2dDevice = _d2dFactory.CreateDevice(_dxgiDevice);
        _dc = _d2dDevice.CreateDeviceContext(DeviceContextOptions.None);
        _dc.AntialiasMode = AntialiasMode.PerPrimitive;

        BindBackBuffer();

        // Brushes from per-scene blade palettes (§13).
        _brushes = new ID2D1SolidColorBrush[Constants.SCENE_COUNT, Constants.PALETTE_SIZE];
        for (int s = 0; s < Constants.SCENE_COUNT; s++)
        {
            for (int i = 0; i < Constants.PALETTE_SIZE; i++)
            {
                _brushes[s, i] = _dc.CreateSolidColorBrush(ArgbToColor4(Constants.SCENE_PALETTES[s, i]));
            }
        }

        // Flower head brushes from FLOWER_PALETTE.
        _flowerHeadBrushes = new ID2D1SolidColorBrush[Constants.FLOWER_PALETTE.Length];
        for (int i = 0; i < Constants.FLOWER_PALETTE.Length; i++)
        {
            _flowerHeadBrushes[i] = _dc.CreateSolidColorBrush(ArgbToColor4(Constants.FLOWER_PALETTE[i]));
        }

        _mushroomCapBrushes = new ID2D1SolidColorBrush[Constants.MUSHROOM_PALETTE.Length];
        for (int i = 0; i < _mushroomCapBrushes.Length; i++)
        {
            _mushroomCapBrushes[i] = _dc.CreateSolidColorBrush(ArgbToColor4(Constants.MUSHROOM_PALETTE[i]));
        }
        _mushroomStemBrush = _dc.CreateSolidColorBrush(ArgbToColor4(Constants.MUSHROOM_STEM_COLOR));
        _cactusBrush = _dc.CreateSolidColorBrush(ArgbToColor4(Constants.CACTUS_COLOR));
        _tumbleweedBrush = _dc.CreateSolidColorBrush(ArgbToColor4(Constants.TUMBLEWEED_COLOR));
        _snowflakeBrush = _dc.CreateSolidColorBrush(ArgbToColor4(Constants.SNOWFLAKE_COLOR));
        _snowTipBrush = _dc.CreateSolidColorBrush(ArgbToColor4(Constants.SNOW_TIP_COLOR));

        // Rounded-cap stroke for blade segments - matches the spec note in §7.
        var ssProps = new StrokeStyleProperties
        {
            StartCap = CapStyle.Round,
            EndCap = CapStyle.Round,
            DashCap = CapStyle.Round,
            LineJoin = LineJoin.Round,
            MiterLimit = 1.0f,
            DashStyle = DashStyle.Solid,
        };
        _strokeStyle = _d2dFactory.CreateStrokeStyle(ssProps);

        // ----- DComp -----
        // DCompositionCreateDevice3<T> constrains T : IDCompositionDevice (v1).
        // We need IDCompositionDesktopDevice, which inherits IDCompositionDevice2
        // but not the v1 IDCompositionDevice. Create the v1 then QI up.
        using (var dcompV1 = DComp.DCompositionCreateDevice3<IDCompositionDevice>(_dxgiDevice))
        {
            _dcompDevice = dcompV1.QueryInterface<IDCompositionDesktopDevice>();
        }
        _dcompDevice.CreateTargetForHwnd(_hwnd, topmost: true, out _dcompTarget).CheckError();
        _dcompVisual = _dcompDevice.CreateVisual();
        _dcompVisual.SetContent(_swapChain);
        _dcompTarget.SetRoot(_dcompVisual);
        _dcompDevice.Commit();
    }

    private void BindBackBuffer()
    {
        using var backBuffer = _swapChain!.GetBuffer<IDXGISurface>(0);

        // DPI = 96 * scale so DIP-space draws are auto-scaled to physical px.
        var bmpProps = new BitmapProperties1
        {
            BitmapOptions = BitmapOptions.Target | BitmapOptions.CannotDraw,
            PixelFormat = new Vortice.DCommon.PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied),
            DpiX = 96f * _dpiScale,
            DpiY = 96f * _dpiScale,
        };
        _targetBitmap?.Dispose();
        _targetBitmap = _dc!.CreateBitmapFromDxgiSurface(backBuffer, bmpProps);
        _dc.Target = _targetBitmap;
    }

    public void Render()
    {
        if (_dc is null || _swapChain is null) return;

        _dc.BeginDraw();
        // Pre-multiplied transparent clear via nullable overload.
        _dc.Clear((Color4?)new Color4(0f, 0f, 0f, 0f));

        var groundY = (float)Sim.GroundY;

        for (int i = 0; i < Sim.Blades.Length; i++)
        {
            ref Blade b = ref Sim.Blades[i];
            DrawBlade(in b, groundY);
        }

        DrawEntities(groundY);

        _dc.EndDraw();
        _swapChain.Present(0, PresentFlags.None);
        _dcompDevice?.Commit();
    }

    private void DrawEntities(float groundY)
    {
        if (Sim.Entities.Count == 0) return;

        foreach (Entity e in Sim.Entities)
        {
            if (e.Kind == EntityKind.Tumbleweed)
            {
                float cx = (float)e.X;
                float cy = (float)e.Y;
                float size = (float)e.Size;
                const double twoPi = Math.PI * 2.0;
                for (int k = 0; k < 5; k++)
                {
                    double angle = e.Rotation + k * (twoPi / 5.0);
                    float dx = (float)Math.Cos(angle);
                    float dy = (float)Math.Sin(angle);
                    float px = -dy;
                    float py = dx;
                    var p0 = new Vector2(cx - dx * size * 0.95f + px * size * 0.18f,
                                         cy - dy * size * 0.95f + py * size * 0.18f);
                    var p1 = new Vector2(cx - dx * size * 0.20f - px * size * 0.14f,
                                         cy - dy * size * 0.20f - py * size * 0.14f);
                    var p2 = new Vector2(cx + dx * size * 0.95f + px * size * 0.18f,
                                         cy + dy * size * 0.95f + py * size * 0.18f);
                    _dc!.DrawLine(p0, p1, _tumbleweedBrush!, 1.0f, _strokeStyle);
                    _dc!.DrawLine(p1, p2, _tumbleweedBrush!, 1.0f, _strokeStyle);
                }
                continue;
            }

            if (e.Kind != EntityKind.Snowflake) continue;
            float r = (float)e.Size;
            var flake = new Ellipse(new Vector2((float)e.X, (float)e.Y), r, r);
            _dc!.FillEllipse(flake, _snowflakeBrush!);
        }
    }

    private void DrawCactusArm(float baseX, float gy, float h, float width, int side)
    {
        float sx = baseX;
        float sy = gy - h * 0.4f;
        float ex = baseX + side * width * 1.5f;
        float ey = gy - h * 0.7f;
        float cx = ex;
        float cy = sy;
        float armWidth = width * 0.7f;

        const int N = 4;
        float prevX = sx;
        float prevY = sy;
        for (int i = 1; i <= N; i++)
        {
            float t = i / (float)N;
            float u = 1.0f - t;
            float px = u * u * sx + 2.0f * u * t * cx + t * t * ex;
            float py = u * u * sy + 2.0f * u * t * cy + t * t * ey;
            _dc!.DrawLine(new Vector2(prevX, prevY), new Vector2(px, py), _cactusBrush!, armWidth, _strokeStyle);
            prevX = px;
            prevY = py;
        }

        _dc!.DrawLine(new Vector2(ex, ey), new Vector2(ex, ey - h * 0.15f), _cactusBrush!, armWidth, _strokeStyle);
    }

    private void DrawBlade(in Blade b, float groundY)
    {
        if (b.IsCactus)
        {
            float baseX = (float)b.BaseX;
            float gy = groundY;
            float width = (float)b.CactusWidth;

            if (b.CutHeight < Constants.CUT_STUMP_THRESHOLD)
            {
                _dc!.DrawLine(new Vector2(baseX, gy),
                              new Vector2(baseX, gy - (float)Constants.STUMP_HEIGHT),
                              _cactusBrush!, width, _strokeStyle);
                return;
            }

            float h = (float)(b.CactusHeight * b.CutHeight);
            float topY = gy - h;
            _dc!.DrawLine(new Vector2(baseX, gy), new Vector2(baseX, topY), _cactusBrush!, width, _strokeStyle);
            _dc.FillEllipse(new Ellipse(new Vector2(baseX, topY), width * 0.5f, width * 0.5f), _cactusBrush!);

            if (b.CactusType == 1)
            {
                DrawCactusArm(baseX, gy, h, width, b.CactusArmSide < 0 ? -1 : 1);
            }
            else if (b.CactusType == 2)
            {
                DrawCactusArm(baseX, gy, h, width, -1);
                DrawCactusArm(baseX, gy, h, width, +1);
            }
            return;
        }

        if (b.IsMushroom)
        {
            float baseX = (float)b.BaseX;
            float gy    = groundY;
            float stemT = (float)b.MushroomStemThickness;

            // Stump stub: when cut below the threshold, draw a short
            // ivory stem with no cap. MUSHROOM_STUMP_HEIGHT is a touch
            // taller than STUMP_HEIGHT so the nub reads as distinct.
            if (b.CutHeight < Constants.CUT_STUMP_THRESHOLD)
            {
                _dc!.DrawLine(
                    new Vector2(baseX, gy),
                    new Vector2(baseX, gy - (float)Constants.MUSHROOM_STUMP_HEIGHT),
                    _mushroomStemBrush!, stemT, _strokeStyle);
                return;
            }

            float scale = (float)b.CutHeight;
            float stemH = (float)b.MushroomStemHeight * scale;
            float capRX = (float)b.MushroomCapWidth   * scale;
            float capRY = (float)b.MushroomCapHeight  * scale;
            float capCY = gy - stemH;

            _dc!.DrawLine(
                new Vector2(baseX, gy),
                new Vector2(baseX, capCY),
                _mushroomStemBrush!, stemT, _strokeStyle);

            int ci = b.MushroomCapColorIdx;
            if ((uint)ci >= (uint)_mushroomCapBrushes!.Length) ci = 0;
            var cap = new Ellipse(new Vector2(baseX, capCY), capRX, capRY);
            _dc.FillEllipse(cap, _mushroomCapBrushes[ci]);
            return;
        }

        var stroke = Sim.ComputeBladeStroke(b, groundY, Sim.CurrentScene);
        int hue = b.Hue;
        if ((uint)hue >= (uint)Constants.PALETTE_SIZE) hue = 0;
        var brush = _brushes![(int)Sim.CurrentScene, hue];

        float bx = (float)stroke.BaseX;
        float by = (float)stroke.BaseY;
        float cx = (float)stroke.CtrlX;
        float cy = (float)stroke.CtrlY;
        float tx = (float)stroke.TipX;
        float ty = (float)stroke.TipY;
        float thickness = (float)stroke.Thickness;

        // Tessellate the quadratic Bezier into 6 line segments. D2D batches
        // DrawLine calls internally so this is cheaper than constructing a
        // path geometry per blade per frame.
        const int N = 6;
        var prevX = bx;
        var prevY = by;
        for (int i = 1; i <= N; i++)
        {
            float t = i / (float)N;
            float u = 1f - t;
            float u2 = u * u;
            float t2 = t * t;
            float ut2 = 2f * u * t;
            float px = u2 * bx + ut2 * cx + t2 * tx;
            float py = u2 * by + ut2 * cy + t2 * ty;
            _dc!.DrawLine(new Vector2(prevX, prevY), new Vector2(px, py), brush, thickness, _strokeStyle);
            prevX = px;
            prevY = py;
        }

        if (b.IsFlower && b.CutHeight >= Constants.CUT_STUMP_THRESHOLD)
        {
            int hi = b.FlowerHeadColorIdx;
            if ((uint)hi >= (uint)_flowerHeadBrushes!.Length) hi = 0;
            float r = (float)b.FlowerHeadRadius;
            var ellipse = new Ellipse(new Vector2(tx, ty), r, r);
            _dc!.FillEllipse(ellipse, _flowerHeadBrushes[hi]);
        }

        if (Sim.CurrentScene == Scene.Winter && !b.IsCactus && b.CutHeight >= Constants.CUT_STUMP_THRESHOLD)
        {
            float r = (float)(b.Thickness * Constants.SNOW_TIP_RADIUS_FACTOR);
            var cap = new Ellipse(new Vector2(tx, ty), r, r);
            _dc!.FillEllipse(cap, _snowTipBrush!);
        }
    }

    private static Color4 ArgbToColor4(uint argb)
    {
        float a = ((argb >> 24) & 0xFF) / 255f;
        float r = ((argb >> 16) & 0xFF) / 255f;
        float g = ((argb >> 8) & 0xFF) / 255f;
        float bl = (argb & 0xFF) / 255f;
        return new Color4(r, g, bl, a);
    }

    public void Dispose()
    {
        try { _strokeStyle?.Dispose(); } catch { }
        if (_brushes is not null)
        {
            for (int s = 0; s < Constants.SCENE_COUNT; s++)
            {
                for (int i = 0; i < Constants.PALETTE_SIZE; i++)
                {
                    try { _brushes[s, i]?.Dispose(); } catch { }
                }
            }
            _brushes = null;
        }
        if (_flowerHeadBrushes is not null)
        {
            foreach (var br in _flowerHeadBrushes)
            {
                try { br?.Dispose(); } catch { }
            }
        }
        if (_mushroomCapBrushes is not null)
        {
            foreach (var br in _mushroomCapBrushes)
            {
                try { br?.Dispose(); } catch { }
            }
        }
        try { _mushroomStemBrush?.Dispose(); } catch { }
        try { _cactusBrush?.Dispose(); } catch { }
        try { _tumbleweedBrush?.Dispose(); } catch { }
        try { _snowflakeBrush?.Dispose(); } catch { }
        try { _snowTipBrush?.Dispose(); } catch { }
        try { _targetBitmap?.Dispose(); } catch { }
        try { _dc?.Dispose(); } catch { }
        try { _d2dDevice?.Dispose(); } catch { }
        try { _d2dFactory?.Dispose(); } catch { }
        try { _dcompVisual?.Dispose(); } catch { }
        try { _dcompTarget?.Dispose(); } catch { }
        try { _dcompDevice?.Dispose(); } catch { }
        try { _swapChain?.Dispose(); } catch { }
        try { _dxgiFactory?.Dispose(); } catch { }
        try { _dxgiDevice?.Dispose(); } catch { }
        try { _d3dDevice?.Dispose(); } catch { }

        if (_hwnd != IntPtr.Zero)
        {
            Win32.DestroyWindow(_hwnd);
        }
    }
}
