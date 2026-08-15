using System.Diagnostics;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace OpenLogicool.CaptureProbe;

// DXGI Desktop Duplication backend: IDXGIOutputDuplication でプライマリ出力から 2 フレーム取得する。
// read-only。他 backend へ黙って fallback しない。
internal static class DuplicationCapture
{
    public static CaptureResult Run(string fileBase)
    {
        var result = ProbeOutput.NewResult("dup", "DXGI Desktop Duplication (IDXGIOutputDuplication)");
        ID3D11Device? device = null;
        IDXGIOutputDuplication? duplication = null;
        try
        {
            var featureLevels = new[] { FeatureLevel.Level_11_1, FeatureLevel.Level_11_0 };
            D3D11.D3D11CreateDevice(
                null,
                DriverType.Hardware,
                DeviceCreationFlags.BgraSupport,
                featureLevels,
                out device).CheckError();

            using var dxgiDevice = device!.QueryInterface<IDXGIDevice>();
            using var adapter = dxgiDevice.GetAdapter();
            adapter.EnumOutputs(0, out var output).CheckError();
            var desc = output.Description;
            using var output1 = output.QueryInterface<IDXGIOutput1>();
            output.Dispose();

            result.Target = new
            {
                MonitorDeviceName = desc.DeviceName,
                DesktopCoordinatesLeft = desc.DesktopCoordinates.Left,
                DesktopCoordinatesTop = desc.DesktopCoordinates.Top,
                DesktopCoordinatesRight = desc.DesktopCoordinates.Right,
                DesktopCoordinatesBottom = desc.DesktopCoordinates.Bottom,
            };

            duplication = output1.DuplicateOutput(device);

            var clock = Stopwatch.StartNew();
            for (var i = 0; i < 2; i++)
            {
                try
                {
                    result.Frames.Add(CaptureFrame(device, duplication, i, clock, fileBase));
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
            duplication?.Dispose();
            device?.Dispose();
        }

        return result;
    }

    private static FrameRecord CaptureFrame(ID3D11Device device, IDXGIOutputDuplication duplication, int sequence, Stopwatch clock, string fileBase)
    {
        duplication.AcquireNextFrame(5000, out _, out var resource).CheckError();
        try
        {
            using var texture = resource.QueryInterface<ID3D11Texture2D>();
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
            resource.Dispose();
            duplication.ReleaseFrame();
        }
    }
}
