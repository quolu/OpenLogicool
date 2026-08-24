using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OpenLogicool.AI;
using OpenLogicool.Capture;
using OpenLogicool.Contracts.Capture;

namespace OpenLogicool.Probe;

internal static class LiveDiscoveryObserveSmoke
{
    private const string ModelId = "qwen3-vl-2b-instruct-cuda-gpu:2";

    public static int Run(string[] arguments, string outputDirectory)
    {
        if (arguments.Length > 1)
        {
            Console.Error.WriteLine("usage: live-discovery-observe [process-name-substring]");
            return 1;
        }

        try
        {
            return RunAsync(
                arguments.Length == 1 ? arguments[0] : "nikke",
                Path.GetFullPath(outputDirectory)).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 2;
        }
    }

    private static async Task<int> RunAsync(string processNameSubstring, string outputDirectory)
    {
        var target = FindWindow(processNameSubstring);
        var daemon = ReadDaemonState();
        Directory.CreateDirectory(outputDirectory);

        using var source = WgcFrameSource.CreateForWindow(
            target.Window,
            $"window:live-discovery:{target.ProcessId}");
        var frame = await WaitForFrameAsync(source, TimeSpan.FromSeconds(10));
        var png = EncodePng(frame);
        var capturedAt = DateTimeOffset.UtcNow;
        var framePath = Path.Combine(
            outputDirectory,
            $"live-discovery-frame-{capturedAt:yyyyMMdd-HHmmss-fff}.png");
        await File.WriteAllBytesAsync(framePath, png);
        var ocr = await WindowsOcrSmoke.RecognizeFrameAsync(frame);

        FoundryVisionResult vision;
        IReadOnlyList<OwnedTcpConnection> connections;
        bool hasNonLoopback;
        await using (var observer = new OwnedTcpConnectionObserver(
            [Environment.ProcessId, daemon.ProcessId]))
        {
            using var client = new FoundryLocalVisionClient(
                daemon.Endpoint,
                ModelId,
                TimeSpan.FromSeconds(20));
            vision = await client.ProposeLabelsAsync(png);
            connections = observer.Observations;
            hasNonLoopback = observer.HasNonLoopbackEstablished;
        }

        var grounded = vision.Labels.Select(label => Ground(label, ocr)).ToArray();
        var hasGroundedCandidate = grounded.Any(item => item.Status == "Grounded");
        var result = new
        {
            SchemaVersion = "1.0.0",
            Probe = "live-discovery-observe",
            CapturedAtUtc = capturedAt,
            Target = new
            {
                target.ProcessId,
                target.ProcessName,
                target.ProcessPath,
                target.PathReadStatus,
                target.WindowTitle,
                WindowRect = target.Rect,
                target.Dpi,
            },
            Frame = new
            {
                frame.SourceId,
                frame.Sequence,
                frame.Width,
                frame.Height,
                frame.PixelFormat,
                frame.DpiX,
                frame.DpiY,
                frame.ColorSpace,
                frame.Rotation,
                frame.TransformRevision,
                frame.FreshnessMs,
                frame.LastChangeMs,
                Sha256 = Convert.ToHexString(SHA256.HashData(png)).ToLowerInvariant(),
                LocalPath = framePath,
            },
            Ocr = new
            {
                Recognizer = "Windows.Media.Ocr",
                ocr.RecognizerLanguage,
                ocr.MaxImageDimension,
                ocr.ElapsedMs,
                ocr.Text,
                ocr.VisualLines,
                ocr.Words,
            },
            Vision = new
            {
                Runtime = $"Microsoft Foundry Local {daemon.Version}",
                Model = ModelId,
                ProviderEndpoint = daemon.Endpoint,
                vision.Status,
                vision.Failure,
                vision.FailureDetail,
                vision.Labels,
                vision.RawOutput,
                vision.ElapsedMs,
                vision.RequestBytes,
                vision.InputTokens,
                vision.OutputTokens,
            },
            GroundedCandidates = grounded,
            Network = new
            {
                StructuralBoundary = "IP literal loopback HTTP only; proxy disabled; redirect disabled",
                ObservedProcessIds = new[] { Environment.ProcessId, daemon.ProcessId },
                Connections = connections,
                NonLoopbackEstablishedCount = connections.Count(item =>
                    item.State == "Established"
                    && !System.Net.IPAddress.IsLoopback(System.Net.IPAddress.Parse(item.RemoteAddress))),
                HasNonLoopbackEstablished = hasNonLoopback,
                ExternalAiTransmissionCount = 0,
                ExternalAiApiKeyCount = 0,
                ExternalAiApiCostUsd = 0,
            },
            Input = new
            {
                DispatchCount = 0,
                Status = "ObserveOnly",
            },
        };

        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
        });
        var outputPath = Path.Combine(
            outputDirectory,
            $"live-discovery-observe-{DateTime.Now:yyyyMMdd-HHmmss-fff}.json");
        await File.WriteAllTextAsync(outputPath, json);
        Console.WriteLine(json);
        Console.WriteLine($"output: {outputPath}");

        return vision.Status == FoundryVisionStatus.Completed
            && hasGroundedCandidate
            && !hasNonLoopback
            ? 0
            : 3;
    }

    private static GroundedCandidate Ground(string label, WindowsOcrSnapshot ocr)
    {
        var matches = ocr.Words
            .Where(word => FrameBoundLabelMatcher.Equals(word.Text, label))
            .ToArray();
        return new GroundedCandidate(
            label,
            matches.Length == 1 ? "Grounded" : "Unknown",
            matches.Length,
            matches.Length == 1 ? matches[0] : null,
            "same-frame exact unique OCR word");
    }

    private static async Task<CapturedFrame> WaitForFrameAsync(
        WgcFrameSource source,
        TimeSpan timeout)
    {
        var started = Stopwatch.StartNew();
        while (started.Elapsed < timeout)
        {
            if (source.PullDetailed().Result is FrameAvailable available)
            {
                return available.Frame;
            }

            await Task.Delay(20);
        }

        throw new TimeoutException("live windowのWGC frameが到着しませんでした。");
    }

    private static byte[] EncodePng(CapturedFrame frame)
    {
        var pixels = frame.Pixels
            ?? throw new InvalidOperationException("capture frame pixelsがありません。");
        var packedStride = checked(frame.Width * 4);
        var packed = new byte[checked(packedStride * frame.Height)];
        for (var y = 0; y < frame.Height; y++)
        {
            pixels.Bgra8.Span.Slice(y * pixels.Stride, packedStride)
                .CopyTo(packed.AsSpan(y * packedStride, packedStride));
        }

        var bitmap = BitmapSource.Create(
            frame.Width,
            frame.Height,
            frame.DpiX,
            frame.DpiY,
            PixelFormats.Bgra32,
            null,
            packed,
            packedStride);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static WindowTarget FindWindow(string processNameSubstring)
    {
        if (string.IsNullOrWhiteSpace(processNameSubstring))
        {
            throw new ArgumentException("process name substringが必要です。", nameof(processNameSubstring));
        }

        var candidates = new List<WindowTarget>();
        EnumWindows((window, _) =>
        {
            if (!IsWindowVisible(window)
                || GetWindowTextLength(window) == 0
                || !GetWindowRect(window, out var rect)
                || rect.Right <= rect.Left
                || rect.Bottom <= rect.Top)
            {
                return true;
            }

            GetWindowThreadProcessId(window, out var processId);
            try
            {
                using var process = Process.GetProcessById(checked((int)processId));
                if (!process.ProcessName.Contains(processNameSubstring, StringComparison.OrdinalIgnoreCase)
                    || process.ProcessName.Contains("launcher", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                var title = new StringBuilder(GetWindowTextLength(window) + 1);
                GetWindowText(window, title, title.Capacity);
                string? processPath = null;
                var pathReadStatus = "Confirmed";
                try
                {
                    processPath = process.MainModule?.FileName;
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
                {
                    pathReadStatus = $"Unknown: {ex.GetType().Name}";
                }

                candidates.Add(new WindowTarget(
                    window,
                    checked((int)processId),
                    process.ProcessName,
                    processPath,
                    pathReadStatus,
                    title.ToString(),
                    new WindowRectangle(rect.Left, rect.Top, rect.Right, rect.Bottom),
                    GetDpiForWindow(window)));
            }
            catch (ArgumentException)
            {
                // EnumWindows後に終了したprocessだけを無視する。
            }

            return true;
        }, IntPtr.Zero);

        return candidates.Count switch
        {
            1 => candidates[0],
            0 => throw new InvalidOperationException(
                $"visible windowを持つprocess '{processNameSubstring}' がありません。"),
            _ => throw new InvalidOperationException(
                $"process '{processNameSubstring}' のvisible windowが{candidates.Count}件あり一意ではありません: "
                + string.Join(" | ", candidates.Select(item => $"{item.ProcessId}:{item.WindowTitle}"))),
        };
    }

    private static FoundryDaemonState ReadDaemonState()
    {
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".foundry",
            "daemon.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        var processId = root.GetProperty("pid").GetInt32();
        var endpoint = new Uri(root.GetProperty("web_urls")[0].GetString()
            ?? throw new InvalidOperationException("Foundry daemon endpointがありません。"));
        using var process = Process.GetProcessById(processId);
        if (!string.Equals(process.ProcessName, "foundrylocald", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Foundry daemon PID {processId} はfoundrylocaldではありません。" );
        }

        var version = root.GetProperty("daemon_version").GetString()
            ?? throw new InvalidOperationException("Foundry daemon versionがありません。");
        return new FoundryDaemonState(processId, endpoint, version);
    }

    private delegate bool EnumWindowsCallback(nint window, nint parameter);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsCallback callback, nint parameter);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint window);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(nint window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(nint window, StringBuilder text, int maxCount);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint window);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(nint window, out NativeRect rect);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private sealed record FoundryDaemonState(int ProcessId, Uri Endpoint, string Version);

    private sealed record GroundedCandidate(
        string Label,
        string Status,
        int MatchCount,
        WindowsOcrWord? Box,
        string Rule);

    private sealed record WindowTarget(
        nint Window,
        int ProcessId,
        string ProcessName,
        string? ProcessPath,
        string PathReadStatus,
        string WindowTitle,
        WindowRectangle Rect,
        uint Dpi);

    private sealed record WindowRectangle(int Left, int Top, int Right, int Bottom);
}
