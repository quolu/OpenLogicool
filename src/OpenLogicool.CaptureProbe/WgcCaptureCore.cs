using System.Diagnostics;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

namespace OpenLogicool.CaptureProbe;

// Windows.Graphics.Capture backend の共通経路（CreateForMonitor / CreateForWindow どちらの
// GraphicsCaptureItem でも同じ）。DispatcherQueue 依存を避けるため CreateFreeThreaded を使い、
// フレーム到着は TryGetNextFrame のポーリングで待つ（イベントループを持たない console app のため）。
internal static class WgcCaptureCore
{
    private const int FrameWaitTimeoutMs = 5000;
    private const int PollIntervalMs = 50;

    public static void RunCapture(CaptureResult result, GraphicsCaptureItem item, string fileBase)
    {
        ID3D11Device? d3dDevice = null;
        IDirect3DDevice? direct3DDevice = null;
        Direct3D11CaptureFramePool? framePool = null;
        GraphicsCaptureSession? session = null;
        try
        {
            var featureLevels = new[] { FeatureLevel.Level_11_1, FeatureLevel.Level_11_0 };
            D3D11.D3D11CreateDevice(
                null,
                DriverType.Hardware,
                DeviceCreationFlags.BgraSupport,
                featureLevels,
                out d3dDevice).CheckError();

            using var dxgiDevice = d3dDevice!.QueryInterface<IDXGIDevice>();
            direct3DDevice = WgcInterop.CreateDirect3DDeviceFromDxgiDevice(dxgiDevice.NativePointer);

            var size = item.Size;
            framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                direct3DDevice, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, size);
            session = framePool.CreateCaptureSession(item);

            session.StartCapture();

            var clock = Stopwatch.StartNew();
            for (var i = 0; i < 2; i++)
            {
                try
                {
                    result.Frames.Add(CaptureFrame(d3dDevice, framePool, i, clock, fileBase));
                }
                catch (Exception ex)
                {
                    result.Frames.Add(ProbeOutput.FailedFrame(i, clock.Elapsed.TotalMilliseconds, ex));
                }
            }
        }
        catch (Exception ex)
        {
            result.Error = ErrorRecord.FromException(ex);
        }
        finally
        {
            session?.Dispose();
            framePool?.Dispose();
            direct3DDevice?.Dispose();
            d3dDevice?.Dispose();
        }
    }

    private static FrameRecord CaptureFrame(ID3D11Device device, Direct3D11CaptureFramePool framePool, int sequence, Stopwatch clock, string fileBase)
    {
        var frame = PollNextFrame(framePool);
        try
        {
            var access = frame.Surface.As<IDirect3DDxgiInterfaceAccess>();
            var texturePtr = access.GetInterface(typeof(ID3D11Texture2D).GUID);
            using var texture = new ID3D11Texture2D(texturePtr);
            var texDesc = texture.Description;

            var stagingDesc = texDesc with
            {
                Usage = ResourceUsage.Staging,
                BindFlags = BindFlags.None,
                CPUAccessFlags = CpuAccessFlags.Read,
                MiscFlags = ResourceOptionFlags.None,
            };
            using var staging = device.CreateTexture2D(stagingDesc);
            device.ImmediateContext.CopyResource(staging, texture);

            var mapped = device.ImmediateContext.Map(staging, 0, Vortice.Direct3D11.MapMode.Read, Vortice.Direct3D11.MapFlags.None);
            try
            {
                var pngPath = Path.Combine(ProbeOutput.OutputDirectory, $"{fileBase}-frame{sequence}.png");
                using var bitmap = ImageUtil.CreateBitmapFromBgra8(mapped.DataPointer, (int)mapped.RowPitch, (int)texDesc.Width, (int)texDesc.Height);
                var luminance = ImageUtil.ComputeAverageLuminanceAndSave(bitmap, pngPath);
                return new FrameRecord
                {
                    Sequence = sequence,
                    MonotonicMs = clock.Elapsed.TotalMilliseconds,
                    Width = (int)texDesc.Width,
                    Height = (int)texDesc.Height,
                    PixelFormat = texDesc.Format.ToString(),
                    AverageLuminance = luminance,
                    PngFile = Path.GetFileName(pngPath),
                };
            }
            finally
            {
                device.ImmediateContext.Unmap(staging, 0);
            }
        }
        finally
        {
            frame.Dispose();
        }
    }

    private static Direct3D11CaptureFrame PollNextFrame(Direct3D11CaptureFramePool framePool)
    {
        var waited = 0;
        while (waited < FrameWaitTimeoutMs)
        {
            var frame = framePool.TryGetNextFrame();
            if (frame is not null)
                return frame;

            Thread.Sleep(PollIntervalMs);
            waited += PollIntervalMs;
        }

        throw new TimeoutException($"no frame arrived within {FrameWaitTimeoutMs}ms");
    }
}
