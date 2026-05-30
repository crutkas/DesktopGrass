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
    private ID2D1SolidColorBrush? _raindropBrush;
    private ID2D1SolidColorBrush? _snowTipBrush;
    private ID2D1SolidColorBrush? _pineBrush;
    private ID2D1SolidColorBrush? _birchBarkBrush;
    private ID2D1SolidColorBrush? _birchMarkBrush;
    // §16 sheep brushes — not scene-keyed (one critter at a time, biome-agnostic).
    private ID2D1SolidColorBrush? _sheepBodyBrush;
    private ID2D1SolidColorBrush? _sheepLegBrush;
    private ID2D1SolidColorBrush? _sheepFaceBrush;
    private ID2D1SolidColorBrush? _sheepEarBrush;
    private ID2D1SolidColorBrush? _sheepInkBrush;
    private ID2D1SolidColorBrush? _catBodyBrush;
    private ID2D1SolidColorBrush? _catLegBrush;
    private ID2D1SolidColorBrush? _catFaceBrush;
    private ID2D1SolidColorBrush? _catEarBrush;
    private ID2D1SolidColorBrush? _catInkBrush;
    private ID2D1SolidColorBrush? _petNameBrush;
    private ID2D1SolidColorBrush? _petNameShadowBrush;
    private ID2D1SolidColorBrush? _dayTintBrush;
    private IDWriteFactory? _dwriteFactory;
    private IDWriteTextFormat? _petNameTextFormat;
    private ID2D1StrokeStyle? _strokeStyle;
    private readonly Dictionary<ulong, double> _petNameLastHover = new();

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
        _raindropBrush = _dc.CreateSolidColorBrush(ArgbToColor4(Constants.RAINDROP_COLOR));
        _snowTipBrush = _dc.CreateSolidColorBrush(ArgbToColor4(Constants.SNOW_TIP_COLOR));
        _pineBrush = _dc.CreateSolidColorBrush(ArgbToColor4(Constants.PINE_COLOR));
        _birchBarkBrush = _dc.CreateSolidColorBrush(ArgbToColor4(Constants.BIRCH_BARK_COLOR));
        _birchMarkBrush = _dc.CreateSolidColorBrush(ArgbToColor4(Constants.BIRCH_MARK_COLOR));

        _sheepBodyBrush = _dc.CreateSolidColorBrush(ArgbToColor4(Constants.SHEEP_BODY_COLOR));
        _sheepLegBrush  = _dc.CreateSolidColorBrush(ArgbToColor4(Constants.SHEEP_LEG_COLOR));
        _sheepFaceBrush = _dc.CreateSolidColorBrush(ArgbToColor4(Constants.SHEEP_FACE_COLOR));
        _sheepEarBrush  = _dc.CreateSolidColorBrush(ArgbToColor4(Constants.SHEEP_EAR_COLOR));
        _sheepInkBrush  = _dc.CreateSolidColorBrush(ArgbToColor4(Constants.SHEEP_INK_COLOR));
        _catBodyBrush = _dc.CreateSolidColorBrush(ArgbToColor4(Constants.CAT_BODY_COLOR));
        _catLegBrush  = _dc.CreateSolidColorBrush(ArgbToColor4(Constants.CAT_LEG_COLOR));
        _catFaceBrush = _dc.CreateSolidColorBrush(ArgbToColor4(Constants.CAT_FACE_COLOR));
        _catEarBrush  = _dc.CreateSolidColorBrush(ArgbToColor4(Constants.CAT_EAR_COLOR));
        _catInkBrush  = _dc.CreateSolidColorBrush(ArgbToColor4(Constants.CAT_INK_COLOR));
        _petNameBrush = _dc.CreateSolidColorBrush(ArgbToColor4(Constants.PET_NAME_COLOR));
        _petNameShadowBrush = _dc.CreateSolidColorBrush(ArgbToColor4(Constants.PET_NAME_SHADOW_COLOR));
        _dayTintBrush = _dc.CreateSolidColorBrush(new Color4(0f, 0f, 0f, 0f));

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

        Vector2? cursorPosition = TryGetCursorPositionDip(out Vector2 cursorDip)
            ? cursorDip
            : null;
        DrawEntities(groundY, cursorPosition);
        ApplyDayTint();

        _dc.EndDraw();
        _swapChain.Present(0, PresentFlags.None);
        _dcompDevice?.Commit();
    }

    private void ApplyDayTint()
    {
        if (!Constants.DAYTINT_ENABLED_DEFAULT || _dc is null || _dayTintBrush is null) return;

        DateTime now = DateTime.Now;
        Vector4 tint = Constants.ComputeDayTint(now.Hour + now.Minute / 60.0);
        if (tint.W <= 0.0f) return;

        _dayTintBrush.Color = new Color4(tint.X, tint.Y, tint.Z, tint.W);
        float widthDip = (float)(Sim.MonitorWidth > 0.0 ? Sim.MonitorWidth : _widthPx / _dpiScale);
        float heightDip = (float)(Sim.WindowHeight > 0.0 ? Sim.WindowHeight : _heightPx / _dpiScale);
        _dc.FillRectangle(new Vortice.RawRectF(0.0f, 0.0f, widthDip, heightDip), _dayTintBrush);
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

            if (e.Kind == EntityKind.Raindrop)
            {
                float x1 = (float)e.X;
                float y1 = (float)e.Y;
                float x2 = (float)(e.X - e.Vx * 0.03);
                float y2 = (float)(e.Y + e.Size);
                _dc!.DrawLine(new Vector2(x1, y1), new Vector2(x2, y2), _raindropBrush!,
                              (float)Constants.RAINDROP_THICKNESS);
                continue;
            }

            if (e.Kind != EntityKind.Snowflake) continue;
            float r = (float)e.Size;
            var flake = new Ellipse(new Vector2((float)e.X, (float)e.Y), r, r);
            _dc!.FillEllipse(flake, _snowflakeBrush!);
        }
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
                            _catLegBrush!, (float)Constants.CAT_TAIL_THICKNESS);
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
                            _catLegBrush!, (float)Constants.CAT_TAIL_THICKNESS);
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
                _dc!.DrawLine(new Vector2(lx, legY0), new Vector2(lx, ly1), _catLegBrush!, 1.2f, _strokeStyle);
            }
        }

        _dc!.FillEllipse(new Ellipse(new Vector2(cx, cy), br, bh), _catBodyBrush!);

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
        _dc!.FillEllipse(new Ellipse(new Vector2(headCx, headCy), headR, headR), _catFaceBrush!);

        float earBaseY = headCy - headR * 0.62f;
        float earH = (float)Constants.CAT_EAR_HEIGHT;
        DrawFilledTriangle(new Vector2(headCx - headR * 0.60f, earBaseY),
                           new Vector2(headCx - headR * 0.18f, earBaseY),
                           new Vector2(headCx - headR * 0.47f - headDirX * 0.65f, earBaseY - earH),
                           _catEarBrush!);
        DrawFilledTriangle(new Vector2(headCx + headR * 0.18f, earBaseY),
                           new Vector2(headCx + headR * 0.60f, earBaseY),
                           new Vector2(headCx + headR * 0.47f + headDirX * 0.65f, earBaseY - earH),
                           _catEarBrush!);

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
                                _catInkBrush!, 0.9f);
            }
        }
        else
        {
            float eyeR = headR * 0.16f;
            _dc!.FillEllipse(new Ellipse(new Vector2(headCx + headDirX * headR * 0.22f,
                                                     headCy - headR * 0.18f), eyeR, eyeR * 0.75f), _catInkBrush!);
            _dc!.FillEllipse(new Ellipse(new Vector2(headCx - headDirX * headR * 0.18f,
                                                     headCy - headR * 0.18f), eyeR, eyeR * 0.75f), _catInkBrush!);
        }

        float noseTipX = headCx + headDirX * headR * 0.63f;
        float noseTipY = headCy + headR * 0.12f;
        DrawFilledTriangle(new Vector2(noseTipX, noseTipY),
                           new Vector2(noseTipX - headDirX * 1.5f, noseTipY - 1.1f),
                           new Vector2(noseTipX - headDirX * 1.5f, noseTipY + 1.1f),
                           _catInkBrush!);

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
                         zSize, 1.0f - t);
            }
        }
    }

    private void DrawPetName(in Entity e, Vector2? cursorPosition)
    {
        if (_dc is null || _petNameTextFormat is null || _petNameBrush is null || _petNameShadowBrush is null)
            return;
        if (e.Kind != EntityKind.Sheep && e.Kind != EntityKind.Cat) return;

        string[] pool = e.Kind == EntityKind.Sheep ? Constants.SHEEP_NAME_POOL : Constants.CAT_NAME_POOL;
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

    private void DrawCatZ(float zX, float zY, float zSize, float alpha)
    {
        _catInkBrush!.Opacity = alpha;
        _dc!.DrawLine(new Vector2(zX, zY), new Vector2(zX + zSize, zY), _catInkBrush!, 0.9f, _strokeStyle);
        _dc!.DrawLine(new Vector2(zX + zSize, zY), new Vector2(zX, zY + zSize), _catInkBrush!, 0.9f, _strokeStyle);
        _dc!.DrawLine(new Vector2(zX, zY + zSize), new Vector2(zX + zSize, zY + zSize), _catInkBrush!, 0.9f, _strokeStyle);
        _catInkBrush!.Opacity = 1.0f;
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

        if (b.IsPine)
        {
            float baseX = (float)b.BaseX;
            float gy = groundY;

            if (b.CutHeight < Constants.CUT_STUMP_THRESHOLD)
            {
                float stumpW = (float)Math.Max(2.0, b.PineWidth * 0.25);
                _dc!.DrawLine(new Vector2(baseX, gy),
                              new Vector2(baseX, gy - (float)Constants.STUMP_HEIGHT),
                              _pineBrush!, stumpW, _strokeStyle);
                return;
            }

            if (b.TreeVariant == 1)
            {
                // ---- Birch: vertical trunk + short bark dashes + upward branch fan ----
                float totalH = (float)(b.PineHeight * b.CutHeight);
                float trunkW = (float)b.PineWidth;
                float trunkTopY = gy - totalH;

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
                return;
            }

            int tierCount = b.PineTierCount > 0 ? b.PineTierCount : Constants.PINE_TIER_COUNT_MIN;
            double totalHd = b.PineHeight * b.CutHeight;
            double tierH = totalHd / tierCount;

            for (int i = 0; i < tierCount; i++)
            {
                double tFrac = (tierCount == 1) ? 0.0 : (double)i / (tierCount - 1);
                double widthAt = b.PineWidth * (1.0 - tFrac * (1.0 - Constants.PINE_TIP_TAPER));
                double baseY = gy - i * tierH * (1.0 - Constants.PINE_TIER_OVERLAP);
                double topY = baseY - tierH;

                DrawFilledPineTri(baseX, (float)baseY, (float)topY, (float)(widthAt * 0.5), _pineBrush!);

                double capHd = tierH * Constants.PINE_SNOW_CAP_FRACTION;
                double capBaseY = topY + capHd;
                double capHalfW = widthAt * 0.5 * Constants.PINE_SNOW_CAP_FRACTION * 1.4;
                DrawFilledPineTri(baseX, (float)capBaseY, (float)topY, (float)capHalfW, _snowTipBrush!);
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

        if (Sim.CurrentScene == Scene.Winter && !b.IsCactus && !b.IsPine && b.CutHeight >= Constants.CUT_STUMP_THRESHOLD)
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
        try { _raindropBrush?.Dispose(); } catch { }
        try { _snowTipBrush?.Dispose(); } catch { }
        try { _pineBrush?.Dispose(); } catch { }
        try { _birchBarkBrush?.Dispose(); } catch { }
        try { _birchMarkBrush?.Dispose(); } catch { }
        try { _sheepBodyBrush?.Dispose(); } catch { }
        try { _sheepLegBrush?.Dispose(); } catch { }
        try { _sheepFaceBrush?.Dispose(); } catch { }
        try { _sheepEarBrush?.Dispose(); } catch { }
        try { _sheepInkBrush?.Dispose(); } catch { }
        try { _catBodyBrush?.Dispose(); } catch { }
        try { _catLegBrush?.Dispose(); } catch { }
        try { _catFaceBrush?.Dispose(); } catch { }
        try { _catEarBrush?.Dispose(); } catch { }
        try { _catInkBrush?.Dispose(); } catch { }
        try { _petNameBrush?.Dispose(); } catch { }
        try { _petNameShadowBrush?.Dispose(); } catch { }
        try { _dayTintBrush?.Dispose(); } catch { }
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
