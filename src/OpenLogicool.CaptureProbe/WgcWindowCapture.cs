namespace OpenLogicool.CaptureProbe;

// Windows.Graphics.Capture backend: タイトル部分一致で見つけた window を CreateForWindow で
// 2 フレーム取得する。read-only。
internal static class WgcWindowCapture
{
    public static CaptureResult Run(string windowTitleSubstring, string fileBase)
    {
        var result = ProbeOutput.NewResult("wgc-window", "Windows.Graphics.Capture (IGraphicsCaptureItemInterop.CreateForWindow)");
        try
        {
            if (string.IsNullOrEmpty(windowTitleSubstring))
                throw new ArgumentException("windowTitleSubstring is required: wgc-window <windowTitleSubstring>");

            var found = Win32Native.FindWindowByTitleSubstring(windowTitleSubstring);
            if (found is null)
                throw new InvalidOperationException($"no visible window with title containing '{windowTitleSubstring}'");

            var (hwnd, title) = found.Value;
            var item = WgcInterop.CreateItemForWindow(hwnd);
            result.Target = new
            {
                Hwnd = hwnd.ToInt64(),
                WindowTitle = title,
                IsIconic = Win32Native.IsIconic(hwnd),
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
