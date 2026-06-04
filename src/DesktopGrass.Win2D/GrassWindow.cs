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
using System.Collections.Generic;
using System.Drawing;
using System.Numerics;
using DesktopGrass.Win2D.Interop;
using Vortice.DCommon;
using Vortice.Direct2D1;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DirectComposition;
using Vortice.DirectWrite;
using Vortice.DXGI;
using Vortice.Mathematics;
using D2DFactoryType = Vortice.Direct2D1.FactoryType;
using DWriteFactoryType = Vortice.DirectWrite.FactoryType;

namespace DesktopGrass.Win2D;

internal sealed class GrassWindow : IDisposable
{
    private readonly IntPtr _hwnd;
    private readonly int _widthPx;
    private readonly int _heightPx;
    private readonly float _dpiScale;
    private readonly Rectangle _monitorBoundsPx;

    private sealed class CatCoatBrushSet
    {
        public ID2D1SolidColorBrush? Body;
        public ID2D1SolidColorBrush? Leg;
        public ID2D1SolidColorBrush? Face;
        public ID2D1SolidColorBrush? Ear;
        public ID2D1SolidColorBrush? Ink;

        public void Dispose()
        {
            try { Body?.Dispose(); } catch { }
            try { Leg?.Dispose(); } catch { }
            try { Face?.Dispose(); } catch { }
            try { Ear?.Dispose(); } catch { }
            try { Ink?.Dispose(); } catch { }
        }
    }

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
    private ID2D1SolidColorBrush[]? _leafBrushes;
    private ID2D1SolidColorBrush? _snowTipBrush;
    private ID2D1SolidColorBrush? _snowBankShadowBrush;
    private ID2D1SolidColorBrush? _pineBrush;
    private ID2D1SolidColorBrush? _pineShadowBrush;
    private ID2D1SolidColorBrush? _pineHighlightBrush;
    private ID2D1SolidColorBrush? _birchBarkBrush;
    private ID2D1SolidColorBrush? _birchMarkBrush;
    private ID2D1SolidColorBrush? _mapleTrunkBrush;
    private ID2D1SolidColorBrush? _mapleTrunkDarkBrush;
    private ID2D1SolidColorBrush[]? _mapleCanopyBrushes;
    private ID2D1SolidColorBrush[]? _coralBrushes;
    private ID2D1SolidColorBrush? _bubbleStrokeBrush;
    private ID2D1SolidColorBrush? _bubbleHighlightBrush;
    private ID2D1SolidColorBrush[]? _fishBrushes;
    private ID2D1SolidColorBrush? _fishFinBrush;
    // §16 sheep brushes — not scene-keyed (one critter at a time, biome-agnostic).
    private ID2D1SolidColorBrush? _sheepBodyBrush;
    private ID2D1SolidColorBrush? _sheepLegBrush;
    private ID2D1SolidColorBrush? _sheepFaceBrush;
    private ID2D1SolidColorBrush? _sheepEarBrush;
    private ID2D1SolidColorBrush? _sheepInkBrush;
    private CatCoatBrushSet[]? _catCoatBrushes;
    private ID2D1SolidColorBrush? _bunnyBodyBrush;
    private ID2D1SolidColorBrush? _bunnyBellyBrush;
    private ID2D1SolidColorBrush? _bunnyEarBrush;
    private ID2D1SolidColorBrush? _bunnyEarInnerBrush;
    private ID2D1SolidColorBrush? _bunnyTailBrush;
    private ID2D1SolidColorBrush? _bunnyEyeBrush;
    private ID2D1SolidColorBrush? _bunnyNoseBrush;
    private ID2D1SolidColorBrush? _hedgehogBodyBrush;
    private ID2D1SolidColorBrush? _hedgehogSpikeBrush;
    private ID2D1SolidColorBrush? _hedgehogSpikeTipBrush;
    private ID2D1SolidColorBrush? _hedgehogNoseBrush;
    private ID2D1SolidColorBrush? _hedgehogEyeBrush;
    private ID2D1SolidColorBrush? _butterflyBodyBrush;
    private ID2D1SolidColorBrush[]? _butterflyWingBrushes;
    private ID2D1SolidColorBrush[]? _butterflyAccentBrushes;
    private ID2D1SolidColorBrush? _fireflyBodyBrush;
    private ID2D1SolidColorBrush? _fireflyGlowBrush;
    private ID2D1SolidColorBrush? _birdBrush;
    private ID2D1SolidColorBrush? _petNameBrush;
    private ID2D1SolidColorBrush? _petNameShadowBrush;
    private IDWriteFactory? _dwriteFactory;
    private IDWriteTextFormat? _petNameTextFormat;
    private ID2D1StrokeStyle? _strokeStyle;
    private readonly Dictionary<ulong, double> _petNameLastHover = new();

    // §CPU Batch A: per-frame grass-blade stroke batching. Plain grass blades are
    // accumulated into a few grouped path geometries keyed by (hue, quantized
    // thickness) and stroked with one DrawGeometry per group instead of ~4
    // DrawLine calls each — collapsing thousands of draw calls into ~36. Tip
    // decorations are deferred to draw on top after all strokes. Mirrors the
    // native renderer (Renderer.cpp DrawGrass).
    private const int BladeBucketCount = 32;
    private const float BladeThicknessBucket = 0.25f;
    private struct BladeGroup
    {
        public ID2D1PathGeometry? Geom;
        public ID2D1GeometrySink? Sink;
        public float Thickness;
    }
    private readonly BladeGroup[,] _bladeGroups =
        new BladeGroup[Constants.PALETTE_SIZE, BladeBucketCount];
    private struct DeferredCap
    {
        public Ellipse Ellipse;
        public ID2D1SolidColorBrush Brush;
    }
    private readonly List<DeferredCap> _deferredCaps = new();

    private const float SheepCuriousVerticalRadiusDip = 120.0f;

    public Sim Sim { get; }
    public IntPtr Hwnd => _hwnd;
    public int WidthPx => _widthPx;
    public int HeightPx => _heightPx;
    public float DpiScale => _dpiScale;
    public Rectangle MonitorBoundsPx => _monitorBoundsPx;

    public void SetScene(Scene s) => Sim.SetScene(s);
    public void SetCritter(CritterKind c) => Sim.SetCritter(c);
    public void SetCritterCount(int n) => Sim.SetCritterCount(n);

    public GrassWindow(IntPtr hwnd, int widthPx, int heightPx, float dpiScale,
                       Rectangle monitorBoundsPx, ulong seed, double monitorWidthDip,
                       double density, double swaySpeed, double swayAmplitude)
    {
        _hwnd = hwnd;
        _widthPx = widthPx;
        _heightPx = heightPx;
        _dpiScale = dpiScale;
        _monitorBoundsPx = monitorBoundsPx;

        Sim = new Sim
        {
            Blades = Sim.GenerateBlades(seed, monitorWidthDip, density),
            GroundY = _heightPx / _dpiScale,
            WindowHeight = _heightPx / _dpiScale,
            SwaySpeedScale = swaySpeed,
            SwayAmpScale = swayAmplitude,
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
        _d2dFactory = D2D1.D2D1CreateFactory<ID2D1Factory1>(D2DFactoryType.SingleThreaded);
        _d2dDevice = _d2dFactory.CreateDevice(_dxgiDevice);
        _dc = _d2dDevice.CreateDeviceContext(DeviceContextOptions.None);
        _dc.AntialiasMode = AntialiasMode.PerPrimitive;

        _dwriteFactory = DWrite.DWriteCreateFactory<IDWriteFactory>(DWriteFactoryType.Shared);
        _petNameTextFormat = _dwriteFactory.CreateTextFormat("Segoe UI", (float)Constants.PET_NAME_FONT_SIZE);
        _petNameTextFormat.TextAlignment = TextAlignment.Center;
        _petNameTextFormat.ParagraphAlignment = ParagraphAlignment.Near;

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
        _leafBrushes = new ID2D1SolidColorBrush[Constants.LEAF_COLOR_COUNT];
        for (int i = 0; i < Constants.LEAF_COLOR_COUNT; i++)
        {
            _leafBrushes[i] = _dc.CreateSolidColorBrush(ArgbToColor4(Constants.LEAF_COLORS[i]));
        }
        _snowTipBrush = _dc.CreateSolidColorBrush(ArgbToColor4(Constants.SNOW_TIP_COLOR));
        _snowBankShadowBrush = _dc.CreateSolidColorBrush(ArgbToColor4(Constants.SNOW_BANK_SHADOW_COLOR));
        _pineBrush = _dc.CreateSolidColorBrush(ArgbToColor4(Constants.PINE_COLOR));
        _pineShadowBrush = _dc.CreateSolidColorBrush(ArgbToColor4(Constants.PINE_SHADOW_COLOR));
        _pineHighlightBrush = _dc.CreateSolidColorBrush(ArgbToColor4(Constants.PINE_HIGHLIGHT_COLOR));
        _birchBarkBrush = _dc.CreateSolidColorBrush(ArgbToColor4(Constants.BIRCH_BARK_COLOR));
        _birchMarkBrush = _dc.CreateSolidColorBrush(ArgbToColor4(Constants.BIRCH_MARK_COLOR));
        _mapleTrunkBrush = _dc.CreateSolidColorBrush(ArgbToColor4(Constants.MAPLE_TRUNK_COLOR));
        _mapleTrunkDarkBrush = _dc.CreateSolidColorBrush(ArgbToColor4(Constants.MAPLE_TRUNK_DARK));
        _mapleCanopyBrushes = new ID2D1SolidColorBrush[Constants.MAPLE_CANOPY_COLOR_COUNT];
        for (int i = 0; i < Constants.MAPLE_CANOPY_COLOR_COUNT; i++)
        {
            _mapleCanopyBrushes[i] = _dc.CreateSolidColorBrush(ArgbToColor4(Constants.MAPLE_CANOPY_COLORS[i]));
        }

        _coralBrushes = new ID2D1SolidColorBrush[Constants.CORAL_COLOR_COUNT];
        for (int i = 0; i < Constants.CORAL_COLOR_COUNT; i++)
        {
            _coralBrushes[i] = _dc.CreateSolidColorBrush(ArgbToColor4(Constants.CORAL_COLORS[i]));
        }
        _bubbleStrokeBrush = _dc.CreateSolidColorBrush(ArgbToColor4(Constants.BUBBLE_STROKE_COLOR));
        _bubbleHighlightBrush = _dc.CreateSolidColorBrush(ArgbToColor4(Constants.BUBBLE_HIGHLIGHT_COLOR));
        _fishBrushes = new ID2D1SolidColorBrush[Constants.FISH_COLOR_COUNT];
        for (int i = 0; i < Constants.FISH_COLOR_COUNT; i++)
        {
            _fishBrushes[i] = _dc.CreateSolidColorBrush(ArgbToColor4(Constants.FISH_COLORS[i]));
        }
        _fishFinBrush = _dc.CreateSolidColorBrush(ArgbToColor4(Constants.FISH_FIN_COLOR));

        _sheepBodyBrush = _dc.CreateSolidColorBrush(ArgbToColor4(Constants.SHEEP_BODY_COLOR));
        _sheepLegBrush  = _dc.CreateSolidColorBrush(ArgbToColor4(Constants.SHEEP_LEG_COLOR));
        _sheepFaceBrush = _dc.CreateSolidColorBrush(ArgbToColor4(Constants.SHEEP_FACE_COLOR));
        _sheepEarBrush  = _dc.CreateSolidColorBrush(ArgbToColor4(Constants.SHEEP_EAR_COLOR));
        _sheepInkBrush  = _dc.CreateSolidColorBrush(ArgbToColor4(Constants.SHEEP_INK_COLOR));
        _catCoatBrushes = new CatCoatBrushSet[Constants.CAT_COAT_VARIANT_COUNT];
        for (int i = 0; i < Constants.CAT_COAT_VARIANT_COUNT; i++)
        {
            var palette = Constants.CAT_COAT_PALETTES[i];
            _catCoatBrushes[i] = new CatCoatBrushSet
            {
                Body = _dc.CreateSolidColorBrush(ArgbToColor4(palette.Body)),
                Leg = _dc.CreateSolidColorBrush(ArgbToColor4(palette.Leg)),
                Face = _dc.CreateSolidColorBrush(ArgbToColor4(palette.Face)),
                Ear = _dc.CreateSolidColorBrush(ArgbToColor4(palette.Ear)),
                Ink = _dc.CreateSolidColorBrush(ArgbToColor4(palette.Ink)),
            };
        }

        _bunnyBodyBrush = _dc.CreateSolidColorBrush(ArgbToColor4(Constants.BUNNY_BODY_COLOR));
        _bunnyBellyBrush = _dc.CreateSolidColorBrush(ArgbToColor4(Constants.BUNNY_BELLY_COLOR));
        _bunnyEarBrush = _dc.CreateSolidColorBrush(ArgbToColor4(Constants.BUNNY_EAR_COLOR));
        _bunnyEarInnerBrush = _dc.CreateSolidColorBrush(ArgbToColor4(Constants.BUNNY_EAR_INNER_COLOR));
        _bunnyTailBrush = _dc.CreateSolidColorBrush(ArgbToColor4(Constants.BUNNY_TAIL_COLOR));
        _bunnyEyeBrush = _dc.CreateSolidColorBrush(ArgbToColor4(Constants.BUNNY_EYE_COLOR));
        _bunnyNoseBrush = _dc.CreateSolidColorBrush(ArgbToColor4(Constants.BUNNY_NOSE_COLOR));

        _hedgehogBodyBrush = _dc.CreateSolidColorBrush(ArgbToColor4(Constants.HEDGEHOG_BODY_COLOR));
        _hedgehogSpikeBrush = _dc.CreateSolidColorBrush(ArgbToColor4(Constants.HEDGEHOG_SPIKE_COLOR));
        _hedgehogSpikeTipBrush = _dc.CreateSolidColorBrush(ArgbToColor4(Constants.HEDGEHOG_SPIKE_TIP_COLOR));
        _hedgehogNoseBrush = _dc.CreateSolidColorBrush(ArgbToColor4(Constants.HEDGEHOG_NOSE_COLOR));
        _hedgehogEyeBrush = _dc.CreateSolidColorBrush(ArgbToColor4(Constants.HEDGEHOG_EYE_COLOR));

        _butterflyBodyBrush = _dc.CreateSolidColorBrush(ArgbToColor4(Constants.BUTTERFLY_BODY_COLOR));
        _butterflyWingBrushes = new ID2D1SolidColorBrush[Constants.BUTTERFLY_COLOR_COUNT];
        _butterflyAccentBrushes = new ID2D1SolidColorBrush[Constants.BUTTERFLY_COLOR_COUNT];
        for (int i = 0; i < Constants.BUTTERFLY_COLOR_COUNT; i++)
        {
            _butterflyWingBrushes[i] = _dc.CreateSolidColorBrush(ArgbToColor4(Constants.BUTTERFLY_PALETTES[i].WingColor));
            _butterflyAccentBrushes[i] = _dc.CreateSolidColorBrush(ArgbToColor4(Constants.BUTTERFLY_PALETTES[i].AccentColor));
        }
        _fireflyBodyBrush = _dc.CreateSolidColorBrush(ArgbToColor4(Constants.FIREFLY_BODY_COLOR));
        _fireflyGlowBrush = _dc.CreateSolidColorBrush(RgbToColor4(Constants.FIREFLY_GLOW_COLOR_RGB));
        _birdBrush = _dc.CreateSolidColorBrush(ArgbToColor4(Constants.BIRD_BODY_COLOR));

        _petNameBrush = _dc.CreateSolidColorBrush(ArgbToColor4(Constants.PET_NAME_COLOR));
        _petNameShadowBrush = _dc.CreateSolidColorBrush(ArgbToColor4(Constants.PET_NAME_SHADOW_COLOR));

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

        // SetTarget does NOT propagate the target bitmap's DPI to the device
        // context, and a context created via CreateDeviceContext defaults to
        // 96 DPI. Without this, DIP-space draws map 1:1 to pixels and the scene
        // is rendered at 96/(96*scale) of the intended size, covering only the
        // left portion of a hi-DPI monitor. Match the native renderer, which
        // sets the context DPI explicitly.
        _dc.SetDpi(96f * _dpiScale, 96f * _dpiScale);
    }

    public void Render()
    {
        if (_dc is null || _swapChain is null) return;

        _dc.BeginDraw();
        // Pre-multiplied transparent clear via nullable overload.
        _dc.Clear((Color4?)new Color4(0f, 0f, 0f, 0f));

        var groundY = (float)Sim.GroundY;

        // Background treeline first (smaller, hazier), so ground cover overlaps
        // their bases and reads as a set-back row.
        if (Sim.CurrentScene == Scene.Winter)
        {
            for (int i = 0; i < Sim.Blades.Length; i++)
            {
                ref Blade b = ref Sim.Blades[i];
                DrawBlade(in b, groundY, i, treesOnly: true, backgroundTrees: true);
            }
        }

        // Ground cover (incl. Winter snow-tipped grass). Plain grass blades are
        // batched into grouped geometries; flushed after the loop.
        _deferredCaps.Clear();
        for (int i = 0; i < Sim.Blades.Length; i++)
        {
            ref Blade b = ref Sim.Blades[i];
            DrawBlade(in b, groundY, i, treesOnly: false, backgroundTrees: false);
        }
        FlushBladeBatch();

        if (Sim.CurrentScene == Scene.Winter || Sim.CurrentScene == Scene.Autumn)
        {
            for (int i = 0; i < Sim.Blades.Length; i++)
            {
                ref Blade b = ref Sim.Blades[i];
                DrawBlade(in b, groundY, i, treesOnly: true, backgroundTrees: false);
            }
        }

        Vector2? cursorPosition = TryGetCursorPositionDip(out Vector2 cursorDip)
            ? cursorDip
            : null;
        DrawEntities(groundY, cursorPosition);

        _dc.EndDraw();
        _swapChain.Present(0, PresentFlags.None);
        _dcompDevice?.Commit();
    }

    private bool TryGetCursorPositionDip(out Vector2 cursorPosition)
    {
        cursorPosition = default;
        if (!Win32.GetCursorPos(out var pt)) return false;

        double windowTopPx = _monitorBoundsPx.Bottom - _heightPx;
        cursorPosition = new Vector2(
            (float)((pt.X - _monitorBoundsPx.Left) / _dpiScale),
            (float)((pt.Y - windowTopPx) / _dpiScale));
        return true;
    }

    private void DrawEntities(float groundY, Vector2? cursorPosition)
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

            if (e.Kind == EntityKind.Sheep)
            {
                DrawSheep(in e, cursorPosition);
                DrawPetName(in e, cursorPosition);
                continue;
            }

            if (e.Kind == EntityKind.Cat)
            {
                DrawCat(in e, cursorPosition);
                DrawPetName(in e, cursorPosition);
                continue;
            }

            if (e.Kind == EntityKind.Bunny)
            {
                DrawBunny(in e);
                DrawPetName(in e, cursorPosition);
                continue;
            }

            if (e.Kind == EntityKind.Hedgehog)
            {
                DrawHedgehog(in e);
                DrawPetName(in e, cursorPosition);
                continue;
            }

            if (e.Kind == EntityKind.Butterfly)
            {
                DrawButterfly(in e);
                continue;
            }

            if (e.Kind == EntityKind.Firefly)
            {
                DrawFirefly(in e);
                continue;
            }

            if (e.Kind == EntityKind.Leaf)
            {
                int idx = e.ColorVariant;
                if ((uint)idx >= (uint)Constants.LEAF_COLOR_COUNT) idx = 0;
                float cx = (float)e.X;
                float cy = (float)e.Y;
                float leafR = (float)e.Size;
                _dc!.FillEllipse(new Ellipse(new Vector2(cx, cy), leafR, leafR * 0.78f), _leafBrushes![idx]);
                float dx = (float)Math.Cos(e.Rotation);
                float dy = (float)Math.Sin(e.Rotation);
                _dc.DrawLine(new Vector2(cx, cy), new Vector2(cx + dx * leafR * 1.25f, cy + dy * leafR * 1.25f),
                             _mapleTrunkDarkBrush!, Math.Max(0.8f, leafR * 0.18f), _strokeStyle);
                continue;
            }

            if (e.Kind == EntityKind.SnowPuff)
            {
                float alpha = 1.0f;
                if (e.Lifetime > 0.0) alpha = (float)(1.0 - e.Age / e.Lifetime);
                if (alpha <= 0.0f) continue;
                if (alpha > 1.0f) alpha = 1.0f;
                float pr = (float)e.Size;
                float pcx = (float)e.X;
                float pcy = (float)e.Y;
                // Cool rim first so the white core reads against the white bank,
                // then the bright core on top.
                if (_snowBankShadowBrush is not null)
                {
                    float sr = pr * (float)Constants.SNOW_PUFF_SHADOW_SCALE;
                    _snowBankShadowBrush.Opacity = alpha * (float)Constants.SNOW_PUFF_SHADOW_OPACITY;
                    _dc!.FillEllipse(new Ellipse(new Vector2(pcx, pcy + pr * (float)Constants.SNOW_PUFF_SHADOW_OFFSET), sr, sr), _snowBankShadowBrush);
                    _snowBankShadowBrush.Opacity = 1.0f;
                }
                _snowflakeBrush!.Opacity = alpha;
                _dc!.FillEllipse(new Ellipse(new Vector2(pcx, pcy), pr, pr), _snowflakeBrush!);
                _snowflakeBrush!.Opacity = 1.0f;
                continue;
            }

            if (e.Kind == EntityKind.Bubble)
            {
                float cx = (float)e.X;
                float cy = (float)e.Y;
                float br = (float)e.Size;
                if (_bubbleStrokeBrush is not null)
                {
                    _dc!.DrawEllipse(new Ellipse(new Vector2(cx, cy), br, br), _bubbleStrokeBrush, Math.Max(0.9f, br * 0.25f));
                }
                if (_bubbleHighlightBrush is not null)
                {
                    float hr = Math.Max(0.6f, br * 0.30f);
                    _dc!.FillEllipse(new Ellipse(new Vector2(cx - br * 0.32f, cy - br * 0.32f), hr, hr), _bubbleHighlightBrush);
                }
                continue;
            }

            if (e.Kind == EntityKind.Fish)
            {
                DrawFish(in e);
                continue;
            }

            if (e.Kind != EntityKind.Snowflake) continue;
            float r = (float)e.Size;
            var flake = new Ellipse(new Vector2((float)e.X, (float)e.Y), r, r);
            _dc!.FillEllipse(flake, _snowflakeBrush!);
        }

        foreach (Entity e in Sim.Entities)
        {
            if (e.Kind == EntityKind.Bird)
            {
                DrawBird(in e);
            }
        }
    }

    private void DrawButterfly(in Entity e)
    {
        if (_butterflyBodyBrush is null || _butterflyWingBrushes is null || _butterflyAccentBrushes is null) return;

        int idx = e.ColorVariant;
        if ((uint)idx >= (uint)Constants.BUTTERFLY_COLOR_COUNT) idx = 0;
        var wingBrush = _butterflyWingBrushes[idx];
        var accentBrush = _butterflyAccentBrushes[idx];

        float cx = (float)e.X;
        float cy = (float)e.Y;
        float wingScale = (float)Constants.ButterflyWingScale(e.Age, e.PhaseY);
        float wingRx = (float)Constants.BUTTERFLY_WING_RADIUS * wingScale;
        float wingRy = (float)Constants.BUTTERFLY_WING_RADIUS * 0.78f;
        float wingOffset = (float)Constants.BUTTERFLY_WING_OFFSET;

        var left = new Vector2(cx - wingOffset, cy);
        var right = new Vector2(cx + wingOffset, cy);
        _dc!.FillEllipse(new Ellipse(left, wingRx, wingRy), wingBrush);
        _dc.FillEllipse(new Ellipse(right, wingRx, wingRy), wingBrush);

        float accentR = Math.Max(0.6f, wingRy * 0.22f);
        _dc.FillEllipse(new Ellipse(new Vector2(left.X - wingRx * 0.35f, left.Y - wingRy * 0.25f), accentR, accentR), accentBrush);
        _dc.FillEllipse(new Ellipse(new Vector2(right.X + wingRx * 0.35f, right.Y - wingRy * 0.25f), accentR, accentR), accentBrush);
        _dc.FillEllipse(new Ellipse(new Vector2(cx, cy), 0.3f, (float)(Constants.BUTTERFLY_BODY_LENGTH * 0.5)), _butterflyBodyBrush);
    }

    private void DrawFirefly(in Entity e)
    {
        if (_fireflyBodyBrush is null || _fireflyGlowBrush is null) return;

        double brightness = Constants.FireflyBlinkBrightness(e.Age, e.BlinkPeriod, e.BlinkPhase);
        if (brightness <= 0.0) return;

        float cx = (float)e.X;
        float cy = (float)e.Y;
        float glowR = (float)(Constants.FIREFLY_GLOW_RADIUS * brightness);
        if (glowR > 0.0f)
        {
            _fireflyGlowBrush.Opacity = (float)((Constants.FIREFLY_GLOW_ALPHA_MAX / 255.0) * brightness);
            _dc!.FillEllipse(new Ellipse(new Vector2(cx, cy), glowR, glowR), _fireflyGlowBrush);
        }

        _fireflyBodyBrush.Opacity = (float)((Constants.FIREFLY_BODY_ALPHA_MAX / 255.0) * brightness);
        _dc!.FillEllipse(new Ellipse(new Vector2(cx, cy), (float)Constants.FIREFLY_BODY_RADIUS, (float)Constants.FIREFLY_BODY_RADIUS), _fireflyBodyBrush);
        _fireflyGlowBrush.Opacity = 1.0f;
        _fireflyBodyBrush.Opacity = 1.0f;
    }

    private void DrawBird(in Entity e)
    {
        if (_birdBrush is null) return;

        double alpha = Constants.BirdFadeAlpha(e.X, e.Vx, Sim.MonitorWidth);
        if (alpha <= 0.0) return;

        float cx = (float)e.X;
        float cy = (float)e.Y;
        float wingScale = (float)Constants.BirdWingScale(e.Age, e.PhaseX);
        float halfSpan = (float)(Constants.BIRD_WING_SPAN * 0.5) * wingScale;
        float wingRise = Math.Max(0.8f, halfSpan * 0.55f);
        float bodyRx = (float)(Constants.BIRD_BODY_LENGTH * 0.5);
        const float bodyRy = 0.75f;

        _birdBrush.Opacity = (float)alpha;
        _dc!.DrawLine(new Vector2(cx, cy), new Vector2(cx - halfSpan, cy - wingRise), _birdBrush, 1.0f);
        _dc.DrawLine(new Vector2(cx, cy), new Vector2(cx + halfSpan, cy - wingRise), _birdBrush, 1.0f);
        _dc.FillEllipse(new Ellipse(new Vector2(cx, cy), bodyRx, bodyRy), _birdBrush);
        _birdBrush.Opacity = 1.0f;
    }

    private void DrawSheep(in Entity e, Vector2? cursorPosition)
    {
        // §16 Suffolk-style vector sheep — white wool cloud + dark head/legs.
        // Pose driven by e.State (Walking / Grazing / Idle / Greeting / Sleeping / Hopping).
        const float twoPi = (float)(Math.PI * 2.0);
        float cx     = (float)e.X;
        float br     = (float)Constants.SHEEP_BODY_RADIUS;
        float bh     = (float)Constants.SHEEP_BODY_HEIGHT;
        float legLen = (float)Constants.SHEEP_LEG_LENGTH;
        float headR  = (float)Constants.SHEEP_HEAD_RADIUS;
        float tailR  = (float)Constants.SHEEP_TAIL_RADIUS;
        float facing = (e.Vx >= 0.0) ? 1.0f : -1.0f;

        bool isWalking  = e.State == Constants.SHEEP_STATE_WALKING;
        bool isGrazing  = e.State == Constants.SHEEP_STATE_GRAZING;
        bool isIdle     = e.State == Constants.SHEEP_STATE_IDLE;
        bool isGreeting = e.State == Constants.SHEEP_STATE_GREETING;
        bool isSleeping = e.State == Constants.SHEEP_STATE_SLEEPING;
        bool isHopping  = e.State == Constants.SHEEP_STATE_HOPPING;

        float hopOffsetY = 0.0f;
        if (isHopping)
        {
            float t = Math.Max(0.0f, Math.Min(1.0f, (float)(e.Age / Constants.SHEEP_HOP_DURATION)));
            hopOffsetY = -4.0f * (float)Constants.SHEEP_HOP_HEIGHT * t * (1.0f - t);
        }
        // Sleeping: body drops by leg-length so it sits on the ground.
        float sleepOffsetY = isSleeping ? legLen : 0.0f;
        float cy = (float)e.Y + hopOffsetY + sleepOffsetY;

        float walkPhase = (float)(e.Age * (twoPi / Constants.SHEEP_WALK_PERIOD));
        float legAmp = isWalking ? (float)Constants.SHEEP_LEG_CYCLE_AMP : 0.0f;
        float headBob = isWalking
            ? (float)(Math.Sin(walkPhase * 2.0f) * Constants.SHEEP_HEAD_BOB_AMP)
            : 0.0f;
        float tailWig = isWalking
            ? (float)(Math.Sin(walkPhase * 2.0f) * Constants.SHEEP_TAIL_WIGGLE_AMP)
            : 0.0f;

        // Legs — hidden when sleeping; static when hopping (suspended look).
        if (!isSleeping)
        {
            float legY0 = cy + bh * 0.30f;
            float[] legXs = { -br * 0.62f, -br * 0.22f, +br * 0.22f, +br * 0.62f };
            float swingA = (float)Math.Sin(walkPhase) * legAmp;
            float swingB = (float)Math.Sin(walkPhase + Math.PI) * legAmp;
            float[] legSwings = { swingA, swingB, swingA, swingB };
            for (int li = 0; li < 4; li++)
            {
                float lx  = cx + legXs[li];
                float ly1 = cy + bh + legLen + legSwings[li];
                _dc!.DrawLine(new Vector2(lx, legY0), new Vector2(lx, ly1),
                              _sheepLegBrush!, 1.8f);
            }
        }

        // Tail puff — rear of the body.
        float tailCx = cx - facing * br * 0.95f + tailWig;
        float tailCy = cy - bh * 0.05f;
        _dc!.FillEllipse(new Ellipse(new Vector2(tailCx, tailCy), tailR, tailR * 0.95f),
                         _sheepBodyBrush!);

        // Body — one large ellipse + 3 evenly-spaced top puffs (cloud silhouette).
        _dc!.FillEllipse(new Ellipse(new Vector2(cx, cy), br, bh), _sheepBodyBrush!);
        float puffY  = cy - bh * 0.55f;
        float puffRx = br * 0.40f;
        float puffRy = bh * 0.48f;
        float[] puffXs = { -br * 0.50f, 0.0f, +br * 0.50f };
        foreach (float pdx in puffXs)
        {
            _dc!.FillEllipse(new Ellipse(new Vector2(cx + pdx, puffY), puffRx, puffRy),
                             _sheepBodyBrush!);
        }

        // Head — position varies by state.
        float headDirX = facing;
        float headDx = headDirX * (br * 1.08f);
        float headDy = -bh * 0.05f + headBob;
        if (isGrazing)
        {
            float munch = (float)(Math.Sin(e.Age * Constants.SHEEP_GRAZE_MUNCH_FREQ)
                                  * Constants.SHEEP_GRAZE_MUNCH_AMP);
            headDx = headDirX * br * 0.85f;
            headDy = bh * 0.85f + munch;
        }
        else if (isIdle)
        {
            float stripTop = (float)(Sim.GroundY - Constants.STRIP_HEIGHT);
            Vector2 cursor = cursorPosition.GetValueOrDefault();
            bool curious = cursorPosition.HasValue
                && Math.Abs(cursor.Y - stripTop) <= SheepCuriousVerticalRadiusDip
                && Math.Abs(cursor.X - cx) <= (float)Constants.SHEEP_CURIOUS_RADIUS;
            if (curious)
            {
                float cursorDx = cursor.X - cx;
                float maxHeadDx = (float)(Constants.SHEEP_CURIOUS_HEAD_TURN_MAX
                                          * Constants.SHEEP_HEAD_RADIUS);
                headDirX = cursorDx >= 0.0f ? 1.0f : -1.0f;
                headDx = Math.Clamp(cursorDx, -maxHeadDx, maxHeadDx);
            }
            else
            {
                float sweep = (float)Math.Sin(e.Age * Constants.SHEEP_IDLE_SWEEP_FREQ);
                headDirX = sweep >= 0.0f ? 1.0f : -1.0f;
                headDx = headDirX * (br * 1.08f) * (0.6f + 0.4f * Math.Abs(sweep));
            }
            headDy = -bh * 0.05f;
        }
        else if (isGreeting)
        {
            headDy -= (float)(Math.Sin(e.Age * Constants.SHEEP_GREET_HEAD_BOB_FREQ)
                              * Constants.SHEEP_GREET_HEAD_BOB_AMP);
        }
        else if (isSleeping)
        {
            headDx = headDirX * br * 0.95f;
            headDy = bh * 0.10f;
        }
        float headCx = cx + headDx;
        float headCy = cy + headDy;

        _dc!.FillEllipse(new Ellipse(new Vector2(headCx, headCy), headR, headR * 1.05f),
                         _sheepFaceBrush!);

        // Ears — two blobs.
        float earRx = headR * 0.32f;
        float earRy = headR * 0.55f;
        _dc!.FillEllipse(new Ellipse(new Vector2(headCx - headR * 0.55f,
                                                  headCy - headR * 0.65f),
                                      earRx, earRy), _sheepEarBrush!);
        _dc!.FillEllipse(new Ellipse(new Vector2(headCx + headR * 0.55f,
                                                  headCy - headR * 0.65f),
                                      earRx, earRy), _sheepEarBrush!);

        // Eye — open dot in most states, closed slit while sleeping.
        if (isSleeping)
        {
            float slitY = headCy - headR * 0.05f;
            float slitX = headCx + headDirX * headR * 0.42f;
            _dc!.DrawLine(new Vector2(slitX - 1.4f, slitY),
                          new Vector2(slitX + 1.4f, slitY),
                          _sheepInkBrush!, 1.0f);
        }
        else
        {
            float eyeR = headR * 0.22f;
            _dc!.FillEllipse(new Ellipse(new Vector2(headCx + headDirX * headR * 0.42f,
                                                      headCy - headR * 0.05f),
                                          eyeR, eyeR), _sheepInkBrush!);
        }

        // Sleeping Z glyphs — two staggered Z's drifting up and growing then
        // fading. Drawn in body-white so they read on any biome.
        if (isSleeping)
        {
            float zBaseX = headCx + headDirX * headR * 0.7f;
            float zBaseY = headCy - headR * 1.4f;
            for (int zi = 0; zi < 2; zi++)
            {
                float phaseOffset = 0.5f * zi;
                float t = (float)(((e.Age / Constants.SHEEP_ZZZ_CYCLE_SEC) + phaseOffset) % 1.0);
                if (t < 0.0f) t += 1.0f;
                float zSize = (float)(Constants.SHEEP_ZZZ_SIZE_START
                                      + t * (Constants.SHEEP_ZZZ_SIZE_END - Constants.SHEEP_ZZZ_SIZE_START));
                float zY = zBaseY - t * (float)Constants.SHEEP_ZZZ_RISE;
                float zX = zBaseX + t * 4.0f * headDirX;
                float alpha = 1.0f - t;
                _sheepBodyBrush!.Opacity = alpha;
                _dc!.DrawLine(new Vector2(zX,         zY),
                              new Vector2(zX + zSize, zY),
                              _sheepBodyBrush!, 1.1f);
                _dc!.DrawLine(new Vector2(zX + zSize, zY),
                              new Vector2(zX,         zY + zSize),
                              _sheepBodyBrush!, 1.1f);
                _dc!.DrawLine(new Vector2(zX,         zY + zSize),
                              new Vector2(zX + zSize, zY + zSize),
                              _sheepBodyBrush!, 1.1f);
            }
            _sheepBodyBrush!.Opacity = 1.0f;
        }
    }

    private void DrawCat(in Entity e, Vector2? cursorPosition)
    {
        var coat = _catCoatBrushes![e.CoatVariantIndex % Constants.CAT_COAT_VARIANT_COUNT];
        var catBodyBrush = coat.Body!;
        var catLegBrush = coat.Leg!;
        var catFaceBrush = coat.Face!;
        var catEarBrush = coat.Ear!;
        var catInkBrush = coat.Ink!;

        const float twoPi = (float)(Math.PI * 2.0);
        float cx = (float)e.X;
        float br = (float)Constants.CAT_BODY_RADIUS;
        float bh = (float)Constants.CAT_BODY_HEIGHT;
        float legLen = (float)Constants.CAT_LEG_LENGTH;
        float headR = (float)Constants.CAT_HEAD_RADIUS;
        float facing = (e.Vx >= 0.0) ? 1.0f : -1.0f;

        bool isWalking = e.State == Constants.CAT_STATE_WALKING;
        bool isIdle = e.State == Constants.CAT_STATE_IDLE;
        bool isSleeping = e.State == Constants.CAT_STATE_SLEEPING;
        bool isPouncing = e.State == Constants.CAT_STATE_POUNCING;

        float pounceOffsetY = 0.0f;
        if (isPouncing)
        {
            float t = Math.Max(0.0f, Math.Min(1.0f, (float)(e.Age / Constants.CAT_POUNCE_DURATION)));
            pounceOffsetY = -4.0f * (float)Constants.CAT_POUNCE_HEIGHT * t * (1.0f - t);
        }
        float sleepOffsetY = isSleeping ? legLen : 0.0f;
        float cy = (float)e.Y + pounceOffsetY + sleepOffsetY;

        float walkPhase = (float)(e.Age * (twoPi / Constants.CAT_WALK_PERIOD));
        float legAmp = isWalking ? (float)Constants.CAT_LEG_CYCLE_AMP : 0.0f;
        float headBob = isWalking
            ? (float)(Math.Sin(walkPhase * 2.0f) * Constants.CAT_HEAD_BOB_AMP)
            : 0.0f;
        float tailSway = (isWalking || isIdle)
            ? (float)(Math.Sin(e.Age * Constants.CAT_TAIL_SWAY_FREQ) * Constants.CAT_TAIL_SWAY_AMP)
            : 0.0f;

        float tailBaseX = cx - facing * br * 0.92f;
        float tailBaseY = cy - bh * 0.10f;
        if (isSleeping)
        {
            DrawCubicBezier(new Vector2(tailBaseX, tailBaseY + bh * 0.15f),
                            new Vector2(cx - facing * br * 0.55f, cy + bh * 0.95f),
                            new Vector2(cx + facing * br * 0.10f, cy + bh * 0.95f),
                            new Vector2(cx + facing * br * 0.72f, cy + bh * 0.45f),
                            catLegBrush, (float)Constants.CAT_TAIL_THICKNESS);
        }
        else
        {
            float tailLen = (float)Constants.CAT_TAIL_LENGTH;
            float tipX = tailBaseX - facing * tailLen * (0.78f + 0.08f * (float)Math.Sin(tailSway));
            float tipY = tailBaseY - tailLen * (0.42f + 0.18f * (float)Math.Cos(tailSway));
            DrawCubicBezier(new Vector2(tailBaseX, tailBaseY),
                            new Vector2(tailBaseX - facing * tailLen * 0.18f, tailBaseY - tailLen * 0.08f),
                            new Vector2(tailBaseX - facing * tailLen * 0.60f, tailBaseY - tailLen * (0.70f + 0.20f * (float)Math.Sin(tailSway))),
                            new Vector2(tipX, tipY),
                            catLegBrush, (float)Constants.CAT_TAIL_THICKNESS);
        }

        if (!isSleeping)
        {
            float legY0 = cy + bh * 0.35f;
            float[] legXs = { -br * 0.58f, -br * 0.20f, br * 0.20f, br * 0.58f };
            float swingA = (float)Math.Sin(walkPhase) * legAmp;
            float swingB = (float)Math.Sin(walkPhase + Math.PI) * legAmp;
            float[] legSwings = { swingA, swingB, swingA, swingB };
            for (int li = 0; li < 4; li++)
            {
                float lx = cx + legXs[li];
                float ly1 = cy + bh + legLen + legSwings[li];
                _dc!.DrawLine(new Vector2(lx, legY0), new Vector2(lx, ly1), catLegBrush, 1.2f, _strokeStyle);
            }
        }

        _dc!.FillEllipse(new Ellipse(new Vector2(cx, cy), br, bh), catBodyBrush);

        float headDirX = facing;
        float headDx = facing * br * 0.82f;
        float headDy = -bh * 0.78f + headBob;
        if (isIdle)
        {
            float stripTop = (float)(Sim.GroundY - Constants.STRIP_HEIGHT);
            Vector2 cursor = cursorPosition.GetValueOrDefault();
            bool curious = cursorPosition.HasValue
                && Math.Abs(cursor.Y - stripTop) <= SheepCuriousVerticalRadiusDip
                && Math.Abs(cursor.X - cx) <= (float)Constants.CAT_CURIOUS_RADIUS;
            if (curious)
            {
                float cursorDx = cursor.X - cx;
                float maxHeadDx = (float)(Constants.CAT_CURIOUS_HEAD_TURN_MAX * Constants.CAT_HEAD_RADIUS);
                headDirX = cursorDx >= 0.0f ? 1.0f : -1.0f;
                headDx = facing * br * 0.55f + Math.Clamp(cursorDx, -maxHeadDx, maxHeadDx);
            }
            else
            {
                float sweep = (float)Math.Sin(e.Age * Constants.CAT_TAIL_SWAY_FREQ * 0.7);
                headDirX = sweep >= 0.0f ? 1.0f : -1.0f;
                headDx = facing * br * 0.60f + sweep * headR * 0.70f;
            }
            headDy = -bh * 0.82f;
        }
        else if (isSleeping)
        {
            headDx = facing * br * 0.62f;
            headDy = -bh * 0.20f;
        }

        float headCx = cx + headDx;
        float headCy = cy + headDy;
        _dc!.FillEllipse(new Ellipse(new Vector2(headCx, headCy), headR, headR), catFaceBrush);

        float earBaseY = headCy - headR * 0.62f;
        float earH = (float)Constants.CAT_EAR_HEIGHT;
        DrawFilledTriangle(new Vector2(headCx - headR * 0.60f, earBaseY),
                           new Vector2(headCx - headR * 0.18f, earBaseY),
                           new Vector2(headCx - headR * 0.47f - headDirX * 0.65f, earBaseY - earH),
                           catEarBrush);
        DrawFilledTriangle(new Vector2(headCx + headR * 0.18f, earBaseY),
                           new Vector2(headCx + headR * 0.60f, earBaseY),
                           new Vector2(headCx + headR * 0.47f + headDirX * 0.65f, earBaseY - earH),
                           catEarBrush);

        if (isSleeping)
        {
            float eyeY = headCy - headR * 0.05f;
            float[] eyeOffsets = { -headR * 0.25f, headR * 0.32f };
            foreach (float ex in eyeOffsets)
            {
                float x0 = headCx + ex - 1.1f;
                float x1 = headCx + ex + 1.1f;
                DrawCubicBezier(new Vector2(x0, eyeY),
                                new Vector2(x0 + 0.45f, eyeY + 0.8f),
                                new Vector2(x1 - 0.45f, eyeY + 0.8f),
                                new Vector2(x1, eyeY),
                                catInkBrush, 0.9f);
            }
        }
        else
        {
            float eyeR = headR * 0.16f;
            _dc!.FillEllipse(new Ellipse(new Vector2(headCx + headDirX * headR * 0.22f,
                                                     headCy - headR * 0.18f), eyeR, eyeR * 0.75f), catInkBrush);
            _dc!.FillEllipse(new Ellipse(new Vector2(headCx - headDirX * headR * 0.18f,
                                                     headCy - headR * 0.18f), eyeR, eyeR * 0.75f), catInkBrush);
        }

        float noseTipX = headCx + headDirX * headR * 0.63f;
        float noseTipY = headCy + headR * 0.12f;
        DrawFilledTriangle(new Vector2(noseTipX, noseTipY),
                           new Vector2(noseTipX - headDirX * 1.5f, noseTipY - 1.1f),
                           new Vector2(noseTipX - headDirX * 1.5f, noseTipY + 1.1f),
                           catInkBrush);

        if (isSleeping)
        {
            float zBaseX = headCx + headDirX * headR * 0.55f;
            float zBaseY = headCy - headR * 1.25f;
            for (int zi = 0; zi < 2; zi++)
            {
                float t = (float)(((e.Age / Constants.SHEEP_ZZZ_CYCLE_SEC) + 0.5 * zi) % 1.0);
                if (t < 0.0f) t += 1.0f;
                float zSize = (float)(Constants.SHEEP_ZZZ_SIZE_START * 0.65
                                      + t * (Constants.SHEEP_ZZZ_SIZE_END * 0.70 - Constants.SHEEP_ZZZ_SIZE_START * 0.65));
                DrawCatZ(zBaseX + t * 3.0f * headDirX,
                         zBaseY - t * (float)(Constants.SHEEP_ZZZ_RISE * 0.75),
                         zSize, 1.0f - t, catInkBrush);
            }
        }
    }

    private void DrawBunny(in Entity e)
    {
        bool isHopping = e.State == Constants.BUNNY_STATE_HOPPING;
        bool isGrazing = e.State == Constants.BUNNY_STATE_GRAZING;
        bool isIdle = e.State == Constants.BUNNY_STATE_IDLE;
        bool isSleeping = e.State == Constants.BUNNY_STATE_SLEEPING;
        bool isStartled = e.State == Constants.BUNNY_STATE_STARTLED;
        float facing = e.Vx >= 0.0 ? 1.0f : -1.0f;
        float hopY = (isHopping || isStartled) ? (float)Sim.BunnyHopYOffset(e.Age, isStartled) : 0.0f;
        float poseLift = isIdle ? 1.5f : (isGrazing ? -1.5f : 0.0f);
        float sleepDrop = isSleeping ? (float)(Constants.BUNNY_LEG_LENGTH + Constants.BUNNY_BODY_HEIGHT * 0.3) : 0.0f;
        float cx = (float)e.X;
        float cy = (float)e.Y - hopY - poseLift + sleepDrop;
        float br = (float)Constants.BUNNY_BODY_RADIUS;
        float bh = (float)Constants.BUNNY_BODY_HEIGHT * (isSleeping ? 0.7f : 1.0f);
        float headR = (float)Constants.BUNNY_HEAD_RADIUS;
        float tailR = (float)Constants.BUNNY_TAIL_RADIUS;

        float tailCx = cx - facing * (br + tailR * 0.35f);
        float tailCy = cy + bh * 0.02f;
        _dc!.FillEllipse(new Ellipse(new Vector2(tailCx, tailCy), tailR, tailR), _bunnyTailBrush!);
        _dc!.FillEllipse(new Ellipse(new Vector2(cx, cy), br, bh), _bunnyBodyBrush!);
        _dc!.FillEllipse(new Ellipse(new Vector2(cx + facing * br * 0.15f, cy + bh * 0.38f),
                                     br * 0.52f, bh * 0.34f), _bunnyBellyBrush!);

        if (!isSleeping && !isHopping && !isStartled)
        {
            float legY = cy + bh * 0.82f;
            float legRx = 1.5f;
            float legRy = (float)(Constants.BUNNY_LEG_LENGTH * 0.35);
            _dc.FillEllipse(new Ellipse(new Vector2(cx - br * 0.35f, legY), legRx, legRy), _bunnyBodyBrush!);
            _dc.FillEllipse(new Ellipse(new Vector2(cx + br * 0.35f, legY), legRx, legRy), _bunnyBodyBrush!);
        }

        float headCx = cx + facing * br * 0.78f;
        float headCy = cy - bh * 0.72f;
        if (isGrazing) headCy = cy + bh * 0.10f;
        if (isSleeping) headCy = cy - bh * 0.05f;
        _dc.FillEllipse(new Ellipse(new Vector2(headCx, headCy), headR, headR), _bunnyBodyBrush!);

        if (isSleeping)
        {
            float earY = headCy - headR * 0.55f;
            for (int i = 0; i < 2; i++)
            {
                float y = earY + i * 1.8f;
                _dc.DrawLine(new Vector2(headCx - facing * headR * 0.25f, y),
                             new Vector2(headCx - facing * (headR + (float)Constants.BUNNY_EAR_HEIGHT * 0.45f), y + 0.7f),
                             _bunnyEarBrush!, (float)Constants.BUNNY_EAR_WIDTH, _strokeStyle);
            }
            float eyeY = headCy - headR * 0.05f;
            _dc.DrawLine(new Vector2(headCx + facing * headR * 0.15f, eyeY),
                         new Vector2(headCx + facing * headR * 0.62f, eyeY),
                         _bunnyEyeBrush!, 0.9f, _strokeStyle);
        }
        else
        {
            float wiggle = isIdle
                ? (float)(Constants.BUNNY_EAR_WIGGLE_AMP * Math.Sin(e.Age * Constants.BUNNY_EAR_WIGGLE_FREQ))
                : 0.0f;
            float earTopY = headCy - headR - (float)Constants.BUNNY_EAR_HEIGHT;
            float earBaseY = headCy - headR * 0.45f;
            float spacing = (float)Constants.BUNNY_EAR_SPACING * 0.5f;
            for (int i = 0; i < 2; i++)
            {
                float side = i == 0 ? -1.0f : 1.0f;
                float lean = side * wiggle;
                float baseX = headCx + side * spacing;
                float topX = baseX + lean * (float)Constants.BUNNY_EAR_HEIGHT;
                _dc.DrawLine(new Vector2(baseX, earBaseY), new Vector2(topX, earTopY),
                             _bunnyEarBrush!, (float)Constants.BUNNY_EAR_WIDTH, _strokeStyle);
                _dc.DrawLine(new Vector2(baseX, earBaseY - 0.8f), new Vector2(topX, earTopY + 1.8f),
                             _bunnyEarInnerBrush!, (float)(Constants.BUNNY_EAR_WIDTH * 0.45), _strokeStyle);
            }
            const float eyeR = 0.9f;
            _dc.FillEllipse(new Ellipse(new Vector2(headCx + facing * headR * 0.35f,
                                                    headCy - headR * 0.12f), eyeR, eyeR), _bunnyEyeBrush!);
        }

        float noseY = headCy + headR * 0.15f
            + (isIdle ? (float)(Constants.BUNNY_NOSE_TWITCH_AMP * Math.Sin(e.Age * Constants.BUNNY_NOSE_TWITCH_FREQ)) : 0.0f);
        _dc.FillEllipse(new Ellipse(new Vector2(headCx + facing * headR * 0.72f, noseY), 1.0f, 0.85f), _bunnyNoseBrush!);

        if (isSleeping)
        {
            float zBaseX = headCx + facing * headR * 0.65f;
            float zBaseY = headCy - headR * 1.3f;
            for (int zi = 0; zi < 2; zi++)
            {
                float t = (float)(((e.Age / Constants.BUNNY_ZZZ_CYCLE_SEC) + 0.5 * zi) % 1.0);
                if (t < 0.0f) t += 1.0f;
                float zSize = (float)(Constants.BUNNY_ZZZ_SIZE_START
                                      + t * (Constants.BUNNY_ZZZ_SIZE_END - Constants.BUNNY_ZZZ_SIZE_START));
                float zX = zBaseX + t * 3.0f * facing;
                float zY = zBaseY - t * (float)Constants.BUNNY_ZZZ_RISE;
                _bunnyTailBrush!.Opacity = 1.0f - t;
                _dc.DrawLine(new Vector2(zX, zY), new Vector2(zX + zSize, zY), _bunnyTailBrush!, 0.9f, _strokeStyle);
                _dc.DrawLine(new Vector2(zX + zSize, zY), new Vector2(zX, zY + zSize), _bunnyTailBrush!, 0.9f, _strokeStyle);
                _dc.DrawLine(new Vector2(zX, zY + zSize), new Vector2(zX + zSize, zY + zSize), _bunnyTailBrush!, 0.9f, _strokeStyle);
            }
            _bunnyTailBrush!.Opacity = 1.0f;
        }
    }

    private void DrawHedgehog(in Entity e)
    {
        if (_hedgehogBodyBrush is null || _hedgehogSpikeBrush is null || _hedgehogSpikeTipBrush is null
            || _hedgehogNoseBrush is null || _hedgehogEyeBrush is null) return;

        float mirrorFacing = e.Vx >= 0.0 ? 1.0f : -1.0f;
        void DrawSpike(float cx, float cy, float radiusX, float radiusY, float angle,
                       bool mirrorX, float spikeLength, float spikeWidth)
        {
            float localUx = MathF.Cos(angle);
            float localUy = MathF.Sin(angle);
            float denom = MathF.Sqrt((localUx * localUx) / (radiusX * radiusX)
                                   + (localUy * localUy) / (radiusY * radiusY));
            float edgeRadius = denom > 0.0f ? 1.0f / denom : radiusX;
            float mirror = mirrorX ? mirrorFacing : 1.0f;
            float ux = mirror * localUx;
            float uy = localUy;
            var basePoint = new Vector2(cx + ux * edgeRadius, cy + uy * edgeRadius);
            var tip = new Vector2(basePoint.X + ux * spikeLength, basePoint.Y + uy * spikeLength);
            float px = -uy;
            float py = ux;
            float half = spikeWidth * 0.5f;
            DrawFilledTriangle(tip,
                               new Vector2(basePoint.X + px * half, basePoint.Y + py * half),
                               new Vector2(basePoint.X - px * half, basePoint.Y - py * half),
                               _hedgehogSpikeBrush);
            _dc!.DrawLine(basePoint, tip, _hedgehogSpikeTipBrush, 0.45f, _strokeStyle);
        }

        bool isSleeping = e.State == Constants.HEDGEHOG_STATE_SLEEPING;
        bool isCurled = e.State == Constants.HEDGEHOG_STATE_CURLED;
        bool isBall = isSleeping || isCurled;
        float cx = (float)e.X;
        float cy = (float)e.Y;

        if (isBall)
        {
            float ballR = (float)(Constants.HEDGEHOG_BODY_RADIUS * 0.85);
            _dc!.FillEllipse(new Ellipse(new Vector2(cx, cy), ballR, ballR), _hedgehogBodyBrush);
            int ballSpikeCount = Constants.HEDGEHOG_SPIKE_COUNT * 3 / 2;
            for (int i = 0; i < ballSpikeCount; i++)
            {
                float angle = (float)(Math.PI * 2.0 * i / ballSpikeCount);
                DrawSpike(cx, cy, ballR, ballR, angle, false,
                          (float)Constants.HEDGEHOG_SPIKE_LENGTH,
                          (float)Constants.HEDGEHOG_SPIKE_WIDTH);
            }

            if (isSleeping)
            {
                for (int zi = 0; zi < 2; zi++)
                {
                    float t = (float)(((e.Age / Constants.HEDGEHOG_ZZZ_CYCLE_SEC) + 0.5 * zi) % 1.0);
                    if (t < 0.0f) t += 1.0f;
                    float zSize = (float)(Constants.HEDGEHOG_ZZZ_SIZE_START
                                          + t * (Constants.HEDGEHOG_ZZZ_SIZE_END - Constants.HEDGEHOG_ZZZ_SIZE_START));
                    float zX = cx + t * 2.4f;
                    float zY = cy - ballR - 3.0f - t * (float)Constants.HEDGEHOG_ZZZ_RISE;
                    _hedgehogSpikeTipBrush.Opacity = 1.0f - t;
                    _dc.DrawLine(new Vector2(zX, zY), new Vector2(zX + zSize, zY), _hedgehogSpikeTipBrush, 0.75f, _strokeStyle);
                    _dc.DrawLine(new Vector2(zX + zSize, zY), new Vector2(zX, zY + zSize), _hedgehogSpikeTipBrush, 0.75f, _strokeStyle);
                    _dc.DrawLine(new Vector2(zX, zY + zSize), new Vector2(zX + zSize, zY + zSize), _hedgehogSpikeTipBrush, 0.75f, _strokeStyle);
                }
                _hedgehogSpikeTipBrush.Opacity = 1.0f;
            }
            return;
        }

        if (e.State == Constants.HEDGEHOG_STATE_WALKING)
        {
            cy += (float)(Constants.HEDGEHOG_WADDLE_AMP * Math.Sin(e.Age * Constants.HEDGEHOG_WADDLE_FREQ));
        }
        else if (e.State == Constants.HEDGEHOG_STATE_IDLE)
        {
            cy -= 1.0f;
        }

        float facing = e.Vx >= 0.0 ? 1.0f : -1.0f;
        float br = (float)Constants.HEDGEHOG_BODY_RADIUS;
        float bh = (float)Constants.HEDGEHOG_BODY_HEIGHT;
        float headR = (float)Constants.HEDGEHOG_HEAD_RADIUS;

        _dc!.FillEllipse(new Ellipse(new Vector2(cx, cy), br, bh), _hedgehogBodyBrush);
        for (int i = 0; i < Constants.HEDGEHOG_SPIKE_COUNT; i++)
        {
            double t = Constants.HEDGEHOG_SPIKE_COUNT > 1
                ? i / (double)(Constants.HEDGEHOG_SPIKE_COUNT - 1)
                : 0.0;
            float degrees = (float)(Constants.HEDGEHOG_SPIKE_ARC_START_DEG
                                    + t * (Constants.HEDGEHOG_SPIKE_ARC_END_DEG - Constants.HEDGEHOG_SPIKE_ARC_START_DEG));
            float angle = degrees * (float)(Math.PI / 180.0);
            DrawSpike(cx, cy, br, bh, angle, true,
                      (float)Constants.HEDGEHOG_SPIKE_LENGTH,
                      (float)Constants.HEDGEHOG_SPIKE_WIDTH);
        }

        float legTopY = cy + bh * 0.72f;
        float legBottomY = cy + bh + (float)Constants.HEDGEHOG_LEG_LENGTH;
        for (int i = 0; i < 4; i++)
        {
            float offset = -br * 0.48f + i * br * 0.32f;
            _dc.DrawLine(new Vector2(cx + offset, legTopY), new Vector2(cx + offset, legBottomY),
                         _hedgehogSpikeBrush, 1.0f, _strokeStyle);
        }

        float snuffleOffset = e.State == Constants.HEDGEHOG_STATE_SNUFFLING
            ? (float)(Constants.HEDGEHOG_SNUFFLE_HEAD_AMP * Math.Sin(e.Age * Constants.HEDGEHOG_SNUFFLE_HEAD_FREQ))
            : 0.0f;
        float headCx = cx + facing * (br * 0.78f) + snuffleOffset;
        float headCy = cy + bh * 0.22f;
        _dc.FillEllipse(new Ellipse(new Vector2(headCx, headCy), headR, headR), _hedgehogBodyBrush);
        _dc.FillEllipse(new Ellipse(new Vector2(headCx + facing * headR * 0.82f, headCy + headR * 0.12f),
                                    (float)Constants.HEDGEHOG_NOSE_RADIUS,
                                    (float)Constants.HEDGEHOG_NOSE_RADIUS), _hedgehogNoseBrush);
        _dc.FillEllipse(new Ellipse(new Vector2(headCx + facing * headR * 0.30f, headCy - headR * 0.35f),
                                    0.75f, 0.75f), _hedgehogEyeBrush);
    }

    private void DrawPetName(in Entity e, Vector2? cursorPosition)
    {
        if (_dc is null || _petNameTextFormat is null || _petNameBrush is null || _petNameShadowBrush is null)
            return;
        if (e.Kind != EntityKind.Sheep && e.Kind != EntityKind.Cat
            && e.Kind != EntityKind.Bunny && e.Kind != EntityKind.Hedgehog) return;

        string[] pool = e.Kind switch
        {
            EntityKind.Cat => Constants.CAT_NAME_POOL,
            EntityKind.Bunny => Constants.BUNNY_NAME_POOL,
            EntityKind.Hedgehog => Constants.HEDGEHOG_NAME_POOL,
            _ => Constants.SHEEP_NAME_POOL,
        };
        if (pool.Length == 0) return;

        ulong key = ((ulong)e.Kind << 32) ^ e.Seed;
        bool hovering = false;
        if (cursorPosition.HasValue)
        {
            Vector2 cursor = cursorPosition.Value;
            double dx = cursor.X - e.X;
            double dy = cursor.Y - e.Y;
            hovering = dx * dx + dy * dy <= Constants.PET_NAME_HOVER_RADIUS * Constants.PET_NAME_HOVER_RADIUS;
        }

        float opacity;
        if (hovering)
        {
            _petNameLastHover[key] = Sim.GlobalTime;
            opacity = 1.0f;
        }
        else
        {
            if (!_petNameLastHover.TryGetValue(key, out double lastHover)) return;
            double elapsed = Sim.GlobalTime - lastHover;
            if (elapsed >= Constants.PET_NAME_FADE_DURATION)
            {
                _petNameLastHover.Remove(key);
                return;
            }
            opacity = (float)(1.0 - elapsed / Constants.PET_NAME_FADE_DURATION);
        }

        string name = pool[e.NameIndex % pool.Length];
        float centerX = (float)e.X;
        float top = (float)(e.Y - e.Size + Constants.PET_NAME_OFFSET_Y - Constants.PET_NAME_FONT_SIZE);
        const float halfWidth = 60.0f;
        float height = (float)(Constants.PET_NAME_FONT_SIZE + 4.0);
        var rect = new Rect(centerX - halfWidth, top, centerX + halfWidth, top + height);
        var shadowRect = new Rect(rect.Left + 1.0f, rect.Top + 1.0f,
                                  rect.Right + 1.0f, rect.Bottom + 1.0f);

        _petNameShadowBrush.Opacity = opacity;
        _petNameBrush.Opacity = opacity;
        _dc.DrawText(name, _petNameTextFormat, shadowRect, _petNameShadowBrush);
        _dc.DrawText(name, _petNameTextFormat, rect, _petNameBrush);
        _petNameShadowBrush.Opacity = 1.0f;
        _petNameBrush.Opacity = 1.0f;
    }

    private void DrawFilledTriangle(Vector2 p0, Vector2 p1, Vector2 p2, ID2D1SolidColorBrush brush)
    {
        float minY = MathF.Floor(Math.Min(p0.Y, Math.Min(p1.Y, p2.Y)));
        float maxY = MathF.Ceiling(Math.Max(p0.Y, Math.Max(p1.Y, p2.Y)));
        const float step = 0.5f;
        for (float y = minY; y <= maxY; y += step)
        {
            int count = 0;
            float minX = float.PositiveInfinity;
            float maxX = float.NegativeInfinity;

            void AddEdge(Vector2 a, Vector2 b)
            {
                if (Math.Abs(a.Y - b.Y) < 0.0001f) return;
                float edgeMinY = Math.Min(a.Y, b.Y);
                float edgeMaxY = Math.Max(a.Y, b.Y);
                if (y < edgeMinY || y > edgeMaxY) return;
                float t = (y - a.Y) / (b.Y - a.Y);
                float x = a.X + t * (b.X - a.X);
                minX = Math.Min(minX, x);
                maxX = Math.Max(maxX, x);
                count++;
            }

            AddEdge(p0, p1);
            AddEdge(p1, p2);
            AddEdge(p2, p0);
            if (count >= 2 && maxX >= minX)
            {
                _dc!.DrawLine(new Vector2(minX, y), new Vector2(maxX, y), brush, step * 1.5f, _strokeStyle);
            }
        }
    }

    private void DrawCubicBezier(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3,
                                 ID2D1SolidColorBrush brush, float thickness)
    {
        const int segments = 8;
        Vector2 prev = p0;
        for (int i = 1; i <= segments; i++)
        {
            float t = i / (float)segments;
            float u = 1.0f - t;
            Vector2 p = u * u * u * p0
                      + 3.0f * u * u * t * p1
                      + 3.0f * u * t * t * p2
                      + t * t * t * p3;
            _dc!.DrawLine(prev, p, brush, thickness, _strokeStyle);
            prev = p;
        }
    }

    private void DrawCatZ(float zX, float zY, float zSize, float alpha, ID2D1SolidColorBrush catInkBrush)
    {
        catInkBrush.Opacity = alpha;
        _dc!.DrawLine(new Vector2(zX, zY), new Vector2(zX + zSize, zY), catInkBrush, 0.9f, _strokeStyle);
        _dc!.DrawLine(new Vector2(zX + zSize, zY), new Vector2(zX, zY + zSize), catInkBrush, 0.9f, _strokeStyle);
        _dc!.DrawLine(new Vector2(zX, zY + zSize), new Vector2(zX + zSize, zY + zSize), catInkBrush, 0.9f, _strokeStyle);
        catInkBrush.Opacity = 1.0f;
    }

    private void DrawFilledPineTri(float cx, float baseY, float topY, float halfW, ID2D1SolidColorBrush brush)
    {
        float h = baseY - topY;
        if (h <= 0.0f || halfW <= 0.0f) return;
        const float kStep = 0.5f;
        for (float y = baseY; y >= topY; y -= kStep)
        {
            float t = (baseY - y) / h;
            float hw = halfW * (1.0f - t);
            if (hw <= 0.0f) continue;
            _dc!.DrawLine(new Vector2(cx - hw, y),
                          new Vector2(cx + hw, y),
                          brush, kStep * 1.5f, _strokeStyle);
        }
    }

    private void DrawCactusArm(float baseX, float gy, float h, float width, int side)
    {
        float sx = baseX;
        float sy = gy - h * 0.4f;
        float ex = baseX + side * width * 1.2f;
        float ey = gy - h * 0.7f;
        float cx = ex;
        float cy = sy;
        float armWidth = width * 0.7f;

        using ID2D1PathGeometry path = _d2dFactory!.CreatePathGeometry();
        using (ID2D1GeometrySink sink = path.Open())
        {
            sink.BeginFigure(new Vector2(sx, sy), FigureBegin.Hollow);
            sink.AddQuadraticBezier(new QuadraticBezierSegment { Point1 = new Vector2(cx, cy), Point2 = new Vector2(ex, ey) });
            sink.AddLine(new Vector2(ex, ey - h * 0.10f));
            sink.EndFigure(FigureEnd.Open);
            sink.Close();
        }
        _dc!.DrawGeometry(path, _cactusBrush!, armWidth, _strokeStyle);
    }

    private void DrawFish(in Entity e)
    {
        if (_fishBrushes is null) return;
        int idx = e.ColorVariant;
        if ((uint)idx >= (uint)Constants.FISH_COLOR_COUNT) idx = 0;
        var body = _fishBrushes[idx];

        float cx = (float)e.X;
        float cy = (float)e.Y;
        float halfLen = (float)e.Size;
        float halfH   = halfLen * 0.55f;
        float facing  = e.Vx >= 0.0 ? 1.0f : -1.0f;

        // Body
        _dc!.FillEllipse(new Ellipse(new Vector2(cx, cy), halfLen, halfH), body);

        // Tail — wobbles with time; tail points away from heading direction.
        double tailAngle = Math.Sin(e.Age * Constants.FISH_TAIL_WOBBLE_FREQ + e.PhaseX) * Constants.FISH_TAIL_WOBBLE_AMP;
        float tailRootX = cx - facing * halfLen * 0.95f;
        float tailLen   = halfLen * 0.80f;
        float tipX      = tailRootX - facing * tailLen * (float)Math.Cos(tailAngle);
        float tipY      = cy + tailLen * (float)Math.Sin(tailAngle);
        float spreadH   = halfH * 0.85f;
        _dc.DrawLine(new Vector2(tailRootX, cy - spreadH * 0.6f), new Vector2(tipX, tipY - spreadH * 0.5f), body, Math.Max(1.0f, halfH * 0.50f), _strokeStyle);
        _dc.DrawLine(new Vector2(tailRootX, cy + spreadH * 0.6f), new Vector2(tipX, tipY + spreadH * 0.5f), body, Math.Max(1.0f, halfH * 0.50f), _strokeStyle);
        _dc.DrawLine(new Vector2(tailRootX, cy), new Vector2(tipX, tipY), body, Math.Max(1.0f, halfH * 0.40f), _strokeStyle);

        // Top fin
        float finBase1X = cx - facing * halfLen * 0.10f;
        float finBase2X = cx + facing * halfLen * 0.15f;
        float finBaseY  = cy - halfH * 0.85f;
        float finTipX   = cx;
        float finTipY   = cy - halfH * 1.55f;
        _dc.DrawLine(new Vector2(finBase1X, finBaseY), new Vector2(finTipX, finTipY), body, Math.Max(0.8f, halfH * 0.30f), _strokeStyle);
        _dc.DrawLine(new Vector2(finBase2X, finBaseY), new Vector2(finTipX, finTipY), body, Math.Max(0.8f, halfH * 0.30f), _strokeStyle);

        // Eye — small dark dot near the head.
        if (_fishFinBrush is not null)
        {
            float eyeX = cx + facing * halfLen * 0.55f;
            float eyeY = cy - halfH * 0.18f;
            float eyeR = Math.Max(0.8f, halfH * 0.18f);
            _dc.FillEllipse(new Ellipse(new Vector2(eyeX, eyeY), eyeR, eyeR), _fishFinBrush);
        }
    }

    private void DrawCoral(in Blade b, float groundY)
    {
        if (_coralBrushes is null) return;
        int idx = b.CoralColorIdx;
        if ((uint)idx >= (uint)Constants.CORAL_COLOR_COUNT) idx = 0;
        var brush = _coralBrushes[idx];

        float baseX = (float)b.BaseX;
        float gy    = groundY;
        float h     = (float)(b.CoralHeight * b.CutHeight);
        float w     = (float)b.CoralWidth;

        if (b.CutHeight < Constants.CUT_STUMP_THRESHOLD)
        {
            float stumpW = Math.Max(2.0f, w * 0.45f);
            _dc!.DrawLine(new Vector2(baseX, gy),
                          new Vector2(baseX, gy - (float)Constants.STUMP_HEIGHT),
                          brush, stumpW, _strokeStyle);
            return;
        }

        switch (b.CoralType)
        {
            case 0:
            {
                // Fan coral — splayed line bundle anchored at the base, tips
                // spread across a horizontal arc at the top.
                const int rays = 7;
                float topY = gy - h;
                float halfW = w * 0.5f;
                float rayW = Math.Max(1.2f, w * 0.10f);
                for (int i = 0; i < rays; i++)
                {
                    float t = (rays == 1) ? 0.5f : (float)i / (rays - 1);
                    float tipX = baseX - halfW + t * w;
                    float tipY = topY + Math.Abs(t - 0.5f) * h * 0.18f;
                    _dc!.DrawLine(new Vector2(baseX, gy), new Vector2(tipX, tipY),
                                  brush, rayW, _strokeStyle);
                    _dc!.FillEllipse(new Ellipse(new Vector2(tipX, tipY), rayW * 0.9f, rayW * 0.9f), brush);
                }
                // Thicker stub at the base to read as anchored mass.
                _dc!.DrawLine(new Vector2(baseX, gy), new Vector2(baseX, gy - h * 0.18f),
                              brush, Math.Max(2.0f, w * 0.30f), _strokeStyle);
                break;
            }
            case 1:
            {
                // Branching coral — central trunk with 3 paired branches.
                float trunkW = Math.Max(1.6f, w * 0.22f);
                float topY = gy - h;
                _dc!.DrawLine(new Vector2(baseX, gy), new Vector2(baseX, topY),
                              brush, trunkW, _strokeStyle);
                _dc!.FillEllipse(new Ellipse(new Vector2(baseX, topY), trunkW * 0.9f, trunkW * 0.9f), brush);
                (float frac, float side, float lenFrac, float angleDeg)[] branches =
                {
                    (0.45f, -1.0f, 0.55f, 55.0f),
                    (0.55f, +1.0f, 0.50f, 50.0f),
                    (0.72f, -1.0f, 0.40f, 35.0f),
                    (0.78f, +1.0f, 0.45f, 40.0f),
                };
                float branchW = Math.Max(1.2f, trunkW * 0.75f);
                foreach (var br in branches)
                {
                    float sx = baseX;
                    float sy = gy - h * br.frac;
                    float blen = h * br.lenFrac;
                    float ang = br.angleDeg * (float)Math.PI / 180.0f;
                    float ex = sx + br.side * blen * (float)Math.Sin(ang);
                    float ey = sy - blen * (float)Math.Cos(ang);
                    _dc!.DrawLine(new Vector2(sx, sy), new Vector2(ex, ey), brush, branchW, _strokeStyle);
                    _dc!.FillEllipse(new Ellipse(new Vector2(ex, ey), branchW * 0.9f, branchW * 0.9f), brush);
                }
                break;
            }
            default:
            {
                // Brain coral — bulbous mound with internal ridge lines.
                float cx = baseX;
                float cy = gy - h * 0.45f;
                float rx = w * 0.55f;
                float ry = h * 0.55f;
                _dc!.FillEllipse(new Ellipse(new Vector2(cx, cy), rx, ry), brush);
                // Ridge lines suggest the brain texture.
                if (_bubbleStrokeBrush is null) break;
                float ridgeW = Math.Max(0.8f, w * 0.06f);
                for (int i = -2; i <= 2; i++)
                {
                    float t = i / 2.0f;
                    float ridgeY = cy + t * ry * 0.55f;
                    float halfRidge = rx * (float)Math.Sqrt(Math.Max(0.0, 1.0 - t * t * 0.85)) * 0.85f;
                    _dc!.DrawLine(new Vector2(cx - halfRidge, ridgeY),
                                  new Vector2(cx + halfRidge, ridgeY),
                                  _bubbleStrokeBrush!, ridgeW, _strokeStyle);
                }
                break;
            }
        }
    }

    private static System.Numerics.Matrix3x2 TreeSwayTransform(in Blade b, double totalH, double pivotGy)
    {
        if (!(totalH > 0.0)) return System.Numerics.Matrix3x2.Identity;
        double apexLean = b.EffectiveLean * Constants.TREE_SWAY_LEAN_FACTOR;
        double maxApex = Constants.TREE_SWAY_MAX_HEIGHT_FRACTION * totalH;
        if (apexLean > maxApex) apexLean = maxApex;
        if (apexLean < -maxApex) apexLean = -maxApex;
        float k = (float)(apexLean / totalH);
        return new System.Numerics.Matrix3x2(1f, 0f, -k, 1f, (float)(k * pivotGy), 0f);
    }

    private void DrawBlade(in Blade b, float groundY, int bladeIndex, bool treesOnly = false, bool backgroundTrees = false)
    {
        if (treesOnly)
        {
            if (!b.IsPine && !b.IsMaple) return;
            // bg pass draws only background pines; fg pass draws the rest
            // (foreground pines + all maples, which are never background).
            bool isBg = b.IsPine && b.TreeBackground;
            if (backgroundTrees != isBg) return;
        }
        else if (b.IsPine || b.IsMaple)
        {
            return;
        }

        if (b.IsCoral)
        {
            DrawCoral(in b, groundY);
            return;
        }

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
            float capR = width * 0.5f * (float)b.CutHeight;
            _dc!.DrawLine(new Vector2(baseX, gy), new Vector2(baseX, topY), _cactusBrush!, width, _strokeStyle);
            _dc.FillEllipse(new Ellipse(new Vector2(baseX, topY), capR, capR), _cactusBrush!);

            if (b.CutHeight >= Constants.CACTUS_ARM_MIN_CUT_HEIGHT)
            {
                if (b.CactusType == 1)
                {
                    DrawCactusArm(baseX, gy, h, width, b.CactusArmSide < 0 ? -1 : 1);
                }
                else if (b.CactusType == 2)
                {
                    DrawCactusArm(baseX, gy, h, width, -1);
                    DrawCactusArm(baseX, gy, h, width, +1);
                }
            }
            return;
        }

        if (b.IsPine)
        {
            float baseX = (float)b.BaseX;
            float gy = groundY;

            // §15.4 depth: background trees shrink toward their base and fade.
            bool bgTree = b.TreeBackground;
            float treeScale = bgTree ? (float)Constants.TREE_BG_SCALE : 1.0f;
            float treeAlpha = bgTree ? Constants.TREE_BG_OPACITY : 1.0f;
            System.Numerics.Matrix3x2 DepthXform(System.Numerics.Matrix3x2 sway) => bgTree
                ? System.Numerics.Matrix3x2.CreateScale(treeScale, treeScale, new Vector2(baseX, gy)) * sway
                : sway;
            void SetTreeAlpha(float a)
            {
                _pineBrush!.Opacity = a;
                _pineShadowBrush!.Opacity = a;
                _snowTipBrush!.Opacity = a;
                _birchBarkBrush!.Opacity = a;
                _birchMarkBrush!.Opacity = a;
            }
            SetTreeAlpha(treeAlpha);

            if (b.CutHeight < Constants.CUT_STUMP_THRESHOLD)
            {
                float stumpW = (float)Math.Max(2.0, b.PineWidth * 0.25) * treeScale;
                _dc!.DrawLine(new Vector2(baseX, gy),
                              new Vector2(baseX, gy - (float)Constants.STUMP_HEIGHT * treeScale),
                              _pineBrush!, stumpW, _strokeStyle);
                SetTreeAlpha(1.0f);
                return;
            }

            if (b.TreeVariant == 1)
            {
                // ---- Birch: vertical trunk + short bark dashes + upward branch fan ----
                float totalH = (float)(b.PineHeight * b.CutHeight);
                float trunkW = (float)b.PineWidth;
                float trunkTopY = gy - totalH;

                _dc!.Transform = DepthXform(TreeSwayTransform(b, totalH, gy));

                _dc!.DrawLine(new Vector2(baseX, gy),
                              new Vector2(baseX, trunkTopY),
                              _birchBarkBrush!, trunkW, _strokeStyle);

                // Short bark dashes — centered, varied lengths, no full ribs.
                float[] dashLenFrac = { 0.50f, 0.30f, 0.45f, 0.25f, 0.40f };
                for (int m = 0; m < Constants.BIRCH_BARK_MARK_COUNT; m++)
                {
                    float tM = (m + 1.0f) / (Constants.BIRCH_BARK_MARK_COUNT + 1.0f);
                    float yM = gy - totalH * tM;
                    float dashLen = trunkW * dashLenFrac[m];
                    _dc.DrawLine(new Vector2(baseX - dashLen * 0.5f, yM),
                                 new Vector2(baseX + dashLen * 0.5f, yM),
                                 _birchMarkBrush!,
                                 Math.Max(1.0f, trunkW * 0.22f),
                                 _strokeStyle);
                }

                // Branch fan — all angled UPWARD, with snow blob at each tip.
                (float trunkFrac, float angleDeg, float side, float lenMul)[] branches = {
                    (0.45f, 35.0f, +1.0f, 1.20f),
                    (0.55f, 50.0f, -1.0f, 1.40f),
                    (0.65f, 25.0f, +1.0f, 1.60f),
                    (0.72f, 60.0f, -1.0f, 1.00f),
                    (0.80f, 20.0f, +1.0f, 1.10f),
                    (0.85f, 45.0f, -1.0f, 0.80f),
                };
                float branchBaseLen = trunkW * 3.0f;
                float branchW = Math.Max(1.0f, trunkW * 0.35f);
                float snowR = Math.Max(1.5f, trunkW * 0.65f);
                foreach (var br in branches)
                {
                    float sy = gy - totalH * br.trunkFrac;
                    float blen = branchBaseLen * br.lenMul;
                    float ang = br.angleDeg * (float)Math.PI / 180.0f;
                    float ex = baseX + br.side * blen * (float)Math.Sin(ang);
                    float ey = sy - blen * (float)Math.Cos(ang);
                    _dc.DrawLine(new Vector2(baseX, sy), new Vector2(ex, ey),
                                 _birchBarkBrush!, branchW, _strokeStyle);
                    _dc.FillEllipse(new Ellipse(new Vector2(ex, ey), snowR, snowR), _snowTipBrush!);
                }

                // Small snow puff right at the top of the trunk.
                float capR = Math.Max(2.0f, trunkW * 0.9f);
                _dc.FillEllipse(new Ellipse(new Vector2(baseX, trunkTopY), capR, capR * 0.6f), _snowTipBrush!);
                _dc.Transform = System.Numerics.Matrix3x2.Identity;
                SetTreeAlpha(1.0f);
                return;
            }

            int tierCount = b.PineTierCount > 0 ? b.PineTierCount : Constants.PINE_TIER_COUNT_MIN;
            double totalHd = b.PineHeight * b.CutHeight;
            double tierH = totalHd / tierCount;

            _dc!.Transform = DepthXform(TreeSwayTransform(b, totalHd, gy));

            for (int i = 0; i < tierCount; i++)
            {
                double tFrac = (tierCount == 1) ? 0.0 : (double)i / (tierCount - 1);
                double widthAt = b.PineWidth * (1.0 - tFrac * (1.0 - Constants.PINE_TIP_TAPER));
                double baseY = gy - i * tierH * (1.0 - Constants.PINE_TIER_OVERLAP);
                double topY = baseY - tierH;
                float halfW = (float)(widthAt * 0.5);

                // Dimensional bough: a self-shadow dropped down-right, the body on
                // top, then a lighter lit face dabbed on the upper-left, so the
                // tier reads as rounded volume instead of a flat triangle.
                float shadowDX = (float)(halfW * Constants.PINE_SHADOW_OFFSET_X_FRAC);
                float shadowDY = (float)(tierH * Constants.PINE_SHADOW_OFFSET_Y_FRAC);
                DrawFilledPineTri(baseX + shadowDX, (float)baseY + shadowDY, (float)topY + shadowDY,
                                  halfW, _pineShadowBrush!);
                DrawFilledPineTri(baseX, (float)baseY, (float)topY, halfW, _pineBrush!);
                _pineHighlightBrush!.Opacity = Constants.PINE_HIGHLIGHT_OPACITY * treeAlpha;
                DrawFilledPineTri(baseX - (float)(halfW * Constants.PINE_HIGHLIGHT_OFFSET_X_FRAC),
                                  (float)baseY, (float)topY,
                                  (float)(halfW * Constants.PINE_HIGHLIGHT_WIDTH_FRAC),
                                  _pineHighlightBrush!);
                _pineHighlightBrush!.Opacity = 1.0f;

                double capHd = tierH * Constants.PINE_SNOW_CAP_FRACTION;
                double capBaseY = topY + capHd;
                double capHalfW = widthAt * 0.5 * Constants.PINE_SNOW_CAP_FRACTION * 1.4;
                DrawFilledPineTri(baseX, (float)capBaseY, (float)topY, (float)capHalfW, _snowTipBrush!);
            }
            _dc.Transform = System.Numerics.Matrix3x2.Identity;
            SetTreeAlpha(1.0f);
            return;
        }

        if (b.IsMaple)
        {
            float baseX = (float)b.BaseX;
            float gy = groundY;
            float trunkW = (float)b.MapleTrunkWidth;

            if (b.CutHeight < Constants.CUT_STUMP_THRESHOLD)
            {
                _dc!.DrawLine(new Vector2(baseX, gy), new Vector2(baseX, gy - (float)Constants.STUMP_HEIGHT),
                              _mapleTrunkBrush!, Math.Max(2.0f, trunkW * 0.65f), _strokeStyle);
                return;
            }

            float totalH = (float)(b.MapleHeight * b.CutHeight);
            float topY = gy - totalH;
            float canopyR = (float)(b.MapleCanopyRadius * b.CutHeight);
            _dc!.Transform = TreeSwayTransform(b, totalH, gy);
            _dc!.DrawLine(new Vector2(baseX, gy), new Vector2(baseX, topY), _mapleTrunkBrush!, trunkW, _strokeStyle);
            _dc.DrawLine(new Vector2(baseX + trunkW * 0.18f, gy - totalH * 0.08f),
                         new Vector2(baseX + trunkW * 0.12f, topY + totalH * 0.15f),
                         _mapleTrunkDarkBrush!, Math.Max(1.0f, trunkW * 0.18f), _strokeStyle);

            (float trunkFrac, float angleDeg, float side, float lenMul)[] branches =
            {
                (0.58f, 55.0f, -1.0f, 0.95f),
                (0.70f, 38.0f, +1.0f, 1.05f),
                (0.82f, 28.0f, -1.0f, 0.70f),
            };
            Span<Vector2> tips = stackalloc Vector2[3];
            float branchBaseLen = Math.Max(trunkW * 2.6f, canopyR * 0.55f);
            float branchW = Math.Max(1.0f, trunkW * 0.32f);
            for (int i = 0; i < branches.Length; i++)
            {
                var br = branches[i];
                float sy = gy - totalH * br.trunkFrac;
                float len = branchBaseLen * br.lenMul;
                float angle = br.angleDeg * (float)Math.PI / 180.0f;
                float ex = baseX + br.side * len * (float)Math.Sin(angle);
                float ey = sy - len * (float)Math.Cos(angle);
                tips[i] = new Vector2(ex, ey);
                _dc.DrawLine(new Vector2(baseX, sy), tips[i], _mapleTrunkDarkBrush!, branchW, _strokeStyle);
            }

            if (!b.MapleIsBare)
            {
                int idx = b.MapleCanopyColorIdx;
                if ((uint)idx >= (uint)Constants.MAPLE_CANOPY_COLOR_COUNT) idx = 0;
                float ccx = baseX;
                float ccy = topY;
                // Layered crown (§16.5): a broad base disc plus several
                // overlapping leaf clumps in staggered autumn tones, giving a
                // full, organic canopy instead of a single flat oval. dx/dy are
                // fractions of canopyR; colorOff cycles the warm palette.
                (float dx, float dy, float r, int colorOff)[] clumps =
                {
                    ( 0.00f, -0.15f, 1.05f, 0),  // back base
                    (-0.50f, -0.05f, 0.60f, 1),
                    ( 0.50f, -0.10f, 0.58f, 2),
                    (-0.28f, -0.48f, 0.54f, 1),
                    ( 0.30f, -0.45f, 0.52f, 2),
                    ( 0.00f, -0.62f, 0.48f, 1),  // top
                    (-0.15f,  0.30f, 0.55f, 0),  // lower-left fill
                    ( 0.22f,  0.28f, 0.50f, 2),  // lower-right fill
                };
                foreach (var c in clumps)
                {
                    var clumpBrush = _mapleCanopyBrushes![(idx + c.colorOff) % Constants.MAPLE_CANOPY_COLOR_COUNT];
                    _dc.FillEllipse(new Ellipse(new Vector2(ccx + canopyR * c.dx, ccy + canopyR * c.dy),
                                                canopyR * c.r, canopyR * c.r * 0.95f), clumpBrush);
                }
                // Two light dabs near the upper-left for a soft sense of light.
                var hi = _mapleCanopyBrushes![(idx + 3) % Constants.MAPLE_CANOPY_COLOR_COUNT];
                _dc.FillEllipse(new Ellipse(new Vector2(ccx - canopyR * 0.34f, ccy - canopyR * 0.34f), canopyR * 0.24f, canopyR * 0.20f), hi);
                _dc.FillEllipse(new Ellipse(new Vector2(ccx - canopyR * 0.05f, ccy - canopyR * 0.58f), canopyR * 0.18f, canopyR * 0.16f), hi);
            }
            else
            {
                for (int i = 0; i < tips.Length; i++)
                {
                    _dc.FillEllipse(new Ellipse(tips[i], 1.8f, 1.8f), _leafBrushes![i % Constants.LEAF_COLOR_COUNT]);
                }
            }
            _dc.Transform = System.Numerics.Matrix3x2.Identity;
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

        // Winter renders ordinary (non-tree) ground cover as snow-tipped grass
        // blades (shared blade path below + the snow-tip cap), so no early-out.

        // §CPU: in Winter, deterministically cull ~25% of plain blades (and their
        // snow caps) to cut the scene's dominant per-frame cost. Keyed on the
        // blade's stable array index so the thinning is steady, not shimmering.
        if (Sim.CurrentScene == Scene.Winter && Constants.WinterBladeCulled((uint)bladeIndex))
            return;

        var stroke = Sim.ComputeBladeStroke(b, groundY, Sim.CurrentScene);
        int hue = b.Hue;
        if ((uint)hue >= (uint)Constants.PALETTE_SIZE) hue = 0;

        float bx = (float)stroke.BaseX;
        float by = (float)stroke.BaseY;
        float cx = (float)stroke.CtrlX;
        float cy = (float)stroke.CtrlY;
        float tx = (float)stroke.TipX;
        float ty = (float)stroke.TipY;
        float thickness = (float)(stroke.Thickness + Constants.BLADE_THICKNESS_RENDER_BONUS);

        // Tessellate the quadratic Bezier into N segments and append them as one
        // open figure (round joins/caps) into the geometry group sharing this
        // blade's hue + quantized thickness. The whole group is stroked with a
        // single DrawGeometry in FlushBladeBatch. Keep in sync with the native
        // renderer.
        int bucket = (int)MathF.Floor(thickness / BladeThicknessBucket + 0.5f);
        if (bucket < 0) bucket = 0;
        if (bucket >= BladeBucketCount) bucket = BladeBucketCount - 1;

        ref BladeGroup g = ref _bladeGroups[hue, bucket];
        if (g.Sink is null)
        {
            g.Geom = _d2dFactory!.CreatePathGeometry();
            g.Sink = g.Geom.Open();
            g.Thickness = bucket * BladeThicknessBucket;
        }

        const int N = 4;
        g.Sink.BeginFigure(new Vector2(bx, by), FigureBegin.Hollow);
        for (int i = 1; i <= N; i++)
        {
            float t = i / (float)N;
            float u = 1f - t;
            float u2 = u * u;
            float t2 = t * t;
            float ut2 = 2f * u * t;
            float px = u2 * bx + ut2 * cx + t2 * tx;
            float py = u2 * by + ut2 * cy + t2 * ty;
            g.Sink.AddLine(new Vector2(px, py));
        }
        g.Sink.EndFigure(FigureEnd.Open);

        if (b.IsFlower && b.CutHeight >= Constants.CUT_STUMP_THRESHOLD)
        {
            int hi = b.FlowerHeadColorIdx;
            if ((uint)hi >= (uint)_flowerHeadBrushes!.Length) hi = 0;
            float r = (float)b.FlowerHeadRadius;
            _deferredCaps.Add(new DeferredCap
            {
                Ellipse = new Ellipse(new Vector2(tx, ty), r, r),
                Brush = _flowerHeadBrushes[hi],
            });
        }

        if (Sim.CurrentScene == Scene.Winter && !b.IsCactus && !b.IsPine && b.CutHeight >= Constants.CUT_STUMP_THRESHOLD)
        {
            float r = (float)(b.Thickness * Constants.SNOW_TIP_RADIUS_FACTOR);
            _deferredCaps.Add(new DeferredCap
            {
                Ellipse = new Ellipse(new Vector2(tx, ty), r, r),
                Brush = _snowTipBrush!,
            });
        }
    }

    // Strokes each accumulated grass-blade group with a single DrawGeometry, then
    // draws deferred tip caps (flower heads, snow caps) on top, and releases the
    // per-frame geometries. Mirrors the native renderer's phase 2 + phase 3.
    private void FlushBladeBatch()
    {
        int sceneIdx = (int)Sim.CurrentScene;
        for (int h = 0; h < Constants.PALETTE_SIZE; h++)
        {
            for (int k = 0; k < BladeBucketCount; k++)
            {
                ref BladeGroup g = ref _bladeGroups[h, k];
                if (g.Sink is null) continue;
                g.Sink.Close();
                _dc!.DrawGeometry(g.Geom!, _brushes![sceneIdx, h], g.Thickness, _strokeStyle);
                g.Sink.Dispose();
                g.Geom!.Dispose();
                g.Sink = null;
                g.Geom = null;
            }
        }

        foreach (DeferredCap d in _deferredCaps)
        {
            _dc!.FillEllipse(d.Ellipse, d.Brush);
        }
        _deferredCaps.Clear();
    }

    private static Color4 ArgbToColor4(uint argb)
    {
        float a = ((argb >> 24) & 0xFF) / 255f;
        float r = ((argb >> 16) & 0xFF) / 255f;
        float g = ((argb >> 8) & 0xFF) / 255f;
        float bl = (argb & 0xFF) / 255f;
        return new Color4(r, g, bl, a);
    }

    private static Color4 RgbToColor4(uint rgb)
    {
        float r = ((rgb >> 16) & 0xFF) / 255f;
        float g = ((rgb >> 8) & 0xFF) / 255f;
        float bl = (rgb & 0xFF) / 255f;
        return new Color4(r, g, bl, 1.0f);
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
        if (_leafBrushes is not null)
        {
            foreach (var br in _leafBrushes)
            {
                try { br?.Dispose(); } catch { }
            }
        }
        try { _snowTipBrush?.Dispose(); } catch { }
        try { _snowBankShadowBrush?.Dispose(); } catch { }
        try { _pineBrush?.Dispose(); } catch { }
        try { _pineShadowBrush?.Dispose(); } catch { }
        try { _pineHighlightBrush?.Dispose(); } catch { }
        try { _birchBarkBrush?.Dispose(); } catch { }
        try { _birchMarkBrush?.Dispose(); } catch { }
        try { _mapleTrunkBrush?.Dispose(); } catch { }
        try { _mapleTrunkDarkBrush?.Dispose(); } catch { }
        if (_mapleCanopyBrushes is not null)
        {
            foreach (var br in _mapleCanopyBrushes)
            {
                try { br?.Dispose(); } catch { }
            }
        }
        try { _sheepBodyBrush?.Dispose(); } catch { }
        try { _sheepLegBrush?.Dispose(); } catch { }
        try { _sheepFaceBrush?.Dispose(); } catch { }
        try { _sheepEarBrush?.Dispose(); } catch { }
        try { _sheepInkBrush?.Dispose(); } catch { }
        if (_catCoatBrushes is not null)
        {
            foreach (var brushes in _catCoatBrushes)
                brushes?.Dispose();
        }
        try { _bunnyBodyBrush?.Dispose(); } catch { }
        try { _bunnyBellyBrush?.Dispose(); } catch { }
        try { _bunnyEarBrush?.Dispose(); } catch { }
        try { _bunnyEarInnerBrush?.Dispose(); } catch { }
        try { _bunnyTailBrush?.Dispose(); } catch { }
        try { _bunnyEyeBrush?.Dispose(); } catch { }
        try { _bunnyNoseBrush?.Dispose(); } catch { }
        try { _hedgehogBodyBrush?.Dispose(); } catch { }
        try { _hedgehogSpikeBrush?.Dispose(); } catch { }
        try { _hedgehogSpikeTipBrush?.Dispose(); } catch { }
        try { _hedgehogNoseBrush?.Dispose(); } catch { }
        try { _hedgehogEyeBrush?.Dispose(); } catch { }
        try { _butterflyBodyBrush?.Dispose(); } catch { }
        if (_butterflyWingBrushes is not null)
        {
            foreach (var br in _butterflyWingBrushes)
            {
                try { br?.Dispose(); } catch { }
            }
        }
        if (_butterflyAccentBrushes is not null)
        {
            foreach (var br in _butterflyAccentBrushes)
            {
                try { br?.Dispose(); } catch { }
            }
        }
        try { _fireflyBodyBrush?.Dispose(); } catch { }
        try { _fireflyGlowBrush?.Dispose(); } catch { }
        try { _birdBrush?.Dispose(); } catch { }
        try { _petNameBrush?.Dispose(); } catch { }
        try { _petNameShadowBrush?.Dispose(); } catch { }
        try { _fishFinBrush?.Dispose(); } catch { }
        if (_fishBrushes is not null)
        {
            foreach (var br in _fishBrushes) { try { br?.Dispose(); } catch { } }
        }
        try { _bubbleHighlightBrush?.Dispose(); } catch { }
        try { _bubbleStrokeBrush?.Dispose(); } catch { }
        if (_coralBrushes is not null)
        {
            foreach (var br in _coralBrushes) { try { br?.Dispose(); } catch { } }
        }
        try { _petNameTextFormat?.Dispose(); } catch { }
        try { _dwriteFactory?.Dispose(); } catch { }
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
