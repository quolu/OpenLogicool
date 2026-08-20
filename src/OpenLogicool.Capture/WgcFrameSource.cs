using System.Runtime.InteropServices;
using OpenLogicool.Contracts.Capture;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

namespace OpenLogicool.Capture;

public sealed record WgcFrameMetadata(
    long Sequence,
    double MonotonicMs,
    DateTimeOffset WallClockUtc,
    int Width,
    int Height,
    string PixelFormat,
    double DpiX,
    double DpiY,
    FramePixels Pixels)
{
    public CapturedFrame ToCapturedFrame(string sourceId) => new(
        "0.2.0",
        sourceId,
        CaptureBackend.WindowsGraphicsCapture,
        Sequence,
        MonotonicMs,
        WallClockUtc,
        Width,
        Height,
        PixelFormat,
        DpiX,
        DpiY,
        TransformRevision: 0,
        FreshnessMs: 0,
        LastChangeMs: 0,
        ColorSpace: FrameColorSpace.Unknown,
        Rotation: FrameRotation.Unknown,
        Crop: new FrameCrop(0, 0, Width, Height),
        Pixels);
}

public sealed class WgcFrameSource : IFrameSource, IDisposable
{
    private const int BufferCount = 2;

    private readonly IntPtr window;
    private readonly string sourceId;
    private readonly ID3D11Device d3dDevice;
    private readonly IDirect3DDevice direct3DDevice;
    private readonly GraphicsCaptureItem item;
    private readonly Direct3D11CaptureFramePool framePool;
    private readonly GraphicsCaptureSession session;
    private long sequence;
    private bool disposed;

    private WgcFrameSource(IntPtr window, string sourceId)
    {
        if (window == IntPtr.Zero)
        {
            throw new ArgumentException("capture 対象 window が必要です。", nameof(window));
        }

        if (string.IsNullOrWhiteSpace(sourceId))
        {
            throw new ArgumentException("capture sourceId が必要です。", nameof(sourceId));
        }

        this.window = window;
        this.sourceId = sourceId;

        var featureLevels = new[] { FeatureLevel.Level_11_1, FeatureLevel.Level_11_0 };
        D3D11.D3D11CreateDevice(
            null,
            DriverType.Hardware,
            DeviceCreationFlags.BgraSupport,
            featureLevels,
            out var createdDevice).CheckError();
        d3dDevice = createdDevice
            ?? throw new InvalidOperationException("D3D11 device が生成されませんでした。");

        using var dxgiDevice = d3dDevice.QueryInterface<IDXGIDevice>();
        direct3DDevice = WgcInterop.CreateDirect3DDeviceFromDxgiDevice(dxgiDevice.NativePointer);
        item = WgcInterop.CreateItemForWindow(window);
        framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
            direct3DDevice,
            DirectXPixelFormat.B8G8R8A8UIntNormalized,
            BufferCount,
            item.Size);
        session = framePool.CreateCaptureSession(item);
        session.StartCapture();
    }

    public static WgcFrameSource CreateForWindow(nint window, string sourceId) =>
        new((IntPtr)window, sourceId);

    public FrameReadResult Pull()
    {
        ThrowIfDisposed();

        var frame = framePool.TryGetNextFrame();
        if (frame is null)
        {
            // WGC は内容の再描画に伴って frame を供給する。静止しているだけなら正常である。
            return new FrameUnavailable("wgc frame はまだ到着していません。");
        }

        using (frame)
        {
            var dpi = WgcInterop.DpiForWindow(window);
            return Capture(frame, dpi);
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        session.Dispose();
        framePool.Dispose();
        direct3DDevice.Dispose();
        d3dDevice.Dispose();
    }

    private FrameReadResult Capture(Direct3D11CaptureFrame frame, double dpi)
    {
        var contentSize = frame.ContentSize;
        var access = frame.Surface.As<IDirect3DDxgiInterfaceAccess>();
        var texturePtr = access.GetInterface(typeof(ID3D11Texture2D).GUID);
        using var texture = new ID3D11Texture2D(texturePtr);
        var textureDescription = texture.Description;
        if (textureDescription.Width != contentSize.Width
            || textureDescription.Height != contentSize.Height)
        {
            framePool.Recreate(
                direct3DDevice,
                DirectXPixelFormat.B8G8R8A8UIntNormalized,
                BufferCount,
                contentSize);
            return new FrameUnavailable("wgc frame pool を content size に合わせて再作成しました。");
        }

        var stagingDescription = textureDescription with
        {
            Usage = ResourceUsage.Staging,
            BindFlags = BindFlags.None,
            CPUAccessFlags = CpuAccessFlags.Read,
            MiscFlags = ResourceOptionFlags.None,
        };

        using var staging = d3dDevice.CreateTexture2D(stagingDescription);
        d3dDevice.ImmediateContext.CopyResource(staging, texture);
        var mapped = d3dDevice.ImmediateContext.Map(
            staging,
            0,
            MapMode.Read,
            Vortice.Direct3D11.MapFlags.None);
        try
        {
            var byteCount = checked((int)(mapped.RowPitch * textureDescription.Height));
            var pixels = new byte[byteCount];
            Marshal.Copy(mapped.DataPointer, pixels, 0, pixels.Length);
            return new FrameAvailable(new WgcFrameMetadata(
                Interlocked.Increment(ref sequence),
                frame.SystemRelativeTime.TotalMilliseconds,
                DateTimeOffset.UtcNow,
                contentSize.Width,
                contentSize.Height,
                textureDescription.Format.ToString(),
                dpi,
                dpi,
                new FramePixels(pixels, checked((int)mapped.RowPitch))).ToCapturedFrame(sourceId));
        }
        finally
        {
            d3dDevice.ImmediateContext.Unmap(staging, 0);
        }
    }

    private void ThrowIfDisposed()
    {
        if (disposed)
        {
            throw new ObjectDisposedException(nameof(WgcFrameSource));
        }
    }
}
