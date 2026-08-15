namespace OpenLogicool.CaptureProbe;

// Windows.Graphics.Capture backend: プライマリモニタを CreateForMonitor (picker なし) で
// 2 フレーム取得する。read-only。
internal static class WgcMonitorCapture
{
    public static CaptureResult Run(string fileBase)
    {
        var result = ProbeOutput.NewResult("wgc-monitor", "Windows.Graphics.Capture (IGraphicsCaptureItemInterop.CreateForMonitor)");
        try
        {
            var hmonitor = Win32Native.GetPrimaryMonitor();
            var item = WgcInterop.CreateItemForMonitor(hmonitor);
            result.Target = new
            {
                HMonitor = hmonitor.ToInt64(),
                ItemDisplayName = item.DisplayName,
                ItemWidth = item.Size.Width,
                ItemHeight = item.Size.Height,
            };

            WgcCaptureCore.RunCapture(result, item, fileBase);
        }
        catch (Exception ex)
        {
            result.Error = ErrorRecord.FromException(ex);
        }

        return result;
    }
}
