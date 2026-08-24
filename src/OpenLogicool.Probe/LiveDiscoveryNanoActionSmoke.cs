using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using OpenLogicool.AI;
using OpenLogicool.Capture;
using OpenLogicool.Contracts.Devices.Shared;
using OpenLogicool.Host;
using OpenLogicool.Input;

namespace OpenLogicool.Probe;

internal static class LiveDiscoveryNanoActionSmoke
{
    public static int Run(string[] arguments, string outputDirectory)
    {
        try
        {
            return RunAsync(arguments, Path.GetFullPath(outputDirectory)).GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 2;
        }
    }

    public static int RunEscape(string[] arguments, string outputDirectory)
    {
        try
        {
            return RunEscapeAsync(arguments, Path.GetFullPath(outputDirectory)).GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 2;
        }
    }

    private static async Task<int> RunEscapeAsync(string[] arguments, string outputDirectory)
    {
        var port = RequiredArgument(arguments, "--port");
        var processName = RequiredArgument(arguments, "--process");
        var label = RequiredArgument(arguments, "--label");
        var observationPath = Path.GetFullPath(RequiredArgument(arguments, "--observation"));
        var pinnedAnchor = ValidatePinnedObservation(observationPath, label);
        Directory.CreateDirectory(outputDirectory);

        var target = LiveDiscoveryObserveSmoke.FindWindow(processName);
        var before = await CaptureAndGroundAsync(target, label, outputDirectory, "escape-before", pinnedAnchor);
        RequireGrounded(before, "Escape前");

        var exchange = new ProbeSerialPortFrameExchange(port);
        using var residentSession = new SerialHidResidentOutputSession(
            exchange,
            new SerialHidSemanticVersion(1, 1, 0),
            TimeSpan.FromMilliseconds(300),
            TimeSpan.FromMilliseconds(50),
            SerialHidProtocolV1.AllCapabilities);
        residentSession.Start();
        var emitter = AssertSerialEmitter(residentSession.Emitter);
        var altTabCount = FocusTargetWithNano(target.Window, emitter);
        var fresh = await CaptureAndGroundAsync(
            target, label, outputDirectory, "escape-fresh", before.Candidate.Box);
        RequireGrounded(fresh, "Escape直前");

        emitter.Emit([Down("Key:Esc")]);
        emitter.Emit([Up("Key:Esc")]);
        var after = await WaitForOutcomeAsync(
            target,
            label,
            outputDirectory,
            expectLabelAbsent: true,
            fresh.Candidate.Box,
            TimeSpan.FromSeconds(10));
        var passed = after.Candidate.Status != "Grounded";

        var evidence = new
        {
            SchemaVersion = "1.0.0",
            Probe = "live-discovery-nano-escape",
            CapturedAtUtc = DateTimeOffset.UtcNow,
            Observation = observationPath,
            Label = label,
            Nano = new
            {
                Port = port,
                FirmwareVersion = residentSession.Protocol.ReadyInfo.FirmwareVersion.ToString(),
                Capabilities = $"0x{(ushort)residentSession.Protocol.ReadyInfo.Capabilities:X4}",
                Route = "NanoSerialHid",
                Fallback = "None",
                AltTabCount = altTabCount,
                Escape = "down/up; matching ACK per state",
                AllUp = true,
            },
            Before = fresh.ToEvidence(),
            After = after.ToEvidence(),
            Policy = "Escape only; no pointer dispatch",
            Input = new
            {
                DispatchRoute = "NanoSerialHid only",
                SendInputDispatchCount = 0,
                ComputerUseDispatchCount = 0,
                ExternalAiTransmissionCount = 0,
                ExternalAiApiCostUsd = 0,
            },
            Passed = passed,
        };
        var outputPath = Path.Combine(
            outputDirectory,
            $"live-discovery-nano-escape-{DateTime.Now:yyyyMMdd-HHmmss-fff}.json");
        await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(evidence, JsonOptions));
        Console.WriteLine(outputPath);
        Console.WriteLine(passed ? "PASS" : "FAIL: Escape後もtarget labelが残りました");
        return passed ? 0 : 3;
    }

    private static async Task<int> RunAsync(string[] arguments, string outputDirectory)
    {
        var port = RequiredArgument(arguments, "--port");
        var processName = RequiredArgument(arguments, "--process");
        var label = RequiredArgument(arguments, "--label");
        var observationPath = Path.GetFullPath(RequiredArgument(arguments, "--observation"));
        var expectLabelAbsent = arguments.Contains("--expect-label-absent", StringComparer.Ordinal);
        var pinnedAnchor = ValidatePinnedObservation(observationPath, label);

        Directory.CreateDirectory(outputDirectory);
        var target = LiveDiscoveryObserveSmoke.FindWindow(processName);
        var before = await CaptureAndGroundAsync(target, label, outputDirectory, "before-focus", pinnedAnchor);
        Console.WriteLine(JsonSerializer.Serialize(new { Stage = "before-focus", before.Candidate }, JsonOptions));
        RequireGrounded(before, "focus前");

        var exchange = new ProbeSerialPortFrameExchange(port);
        using var residentSession = new SerialHidResidentOutputSession(
            exchange,
            new SerialHidSemanticVersion(1, 1, 0),
            TimeSpan.FromMilliseconds(300),
            TimeSpan.FromMilliseconds(50),
            SerialHidProtocolV1.AllCapabilities);
        residentSession.Start();
        var session = residentSession.Protocol;
        session.SendAllUp();
        var emitter = AssertSerialEmitter(residentSession.Emitter);

        var altTabCount = FocusTargetWithNano(target.Window, emitter);
        var grounded = await CaptureAndGroundAsync(
            target, label, outputDirectory, "grounded", before.Candidate.Box);
        Console.WriteLine(JsonSerializer.Serialize(new { Stage = "grounded", grounded.Candidate }, JsonOptions));
        RequireGrounded(grounded, "pointer移動前");

        var pointer = new SerialHidRelativePointer(session, new Win32CursorOracle());
        var firstTarget = MapToScreen(target.CaptureRect, grounded.FrameWidth, grounded.FrameHeight, grounded.Candidate!.Box!);
        var firstMove = pointer.MoveTo(firstTarget);

        var clickFrame = await CaptureAndGroundAsync(
            target, label, outputDirectory, "click-frame", grounded.Candidate.Box);
        Console.WriteLine(JsonSerializer.Serialize(new { Stage = "click-frame", clickFrame.Candidate }, JsonOptions));
        RequireGrounded(clickFrame, "click直前");
        var finalTarget = MapToScreen(target.CaptureRect, clickFrame.FrameWidth, clickFrame.FrameHeight, clickFrame.Candidate!.Box!);
        SerialHidPointerMoveReceipt? correctionMove = null;
        if (!Contains(clickFrame, target.CaptureRect, new Win32CursorOracle().ReadCurrent()))
        {
            correctionMove = pointer.MoveTo(finalTarget);
            clickFrame = await CaptureAndGroundAsync(
                target, label, outputDirectory, "click-frame-corrected", clickFrame.Candidate.Box);
            RequireGrounded(clickFrame, "補正後click直前");
            if (!Contains(clickFrame, target.CaptureRect, new Win32CursorOracle().ReadCurrent()))
            {
                throw new InvalidOperationException("Nano cursorがfresh OCR target矩形内にありません。clickせず停止します。");
            }
        }

        emitter.Emit([Down("Mouse:Left")]);
        emitter.Emit([Up("Mouse:Left")]);
        var after = await WaitForOutcomeAsync(
            target,
            label,
            outputDirectory,
            expectLabelAbsent,
            clickFrame.Candidate.Box,
            TimeSpan.FromSeconds(20));

        var passed = !expectLabelAbsent || after.Candidate?.Status != "Grounded";
        var evidence = new
        {
            SchemaVersion = "1.0.0",
            Probe = "live-discovery-nano-action",
            CapturedAtUtc = DateTimeOffset.UtcNow,
            Observation = observationPath,
            Label = label,
            Target = new
            {
                target.ProcessId,
                target.ProcessName,
                target.WindowTitle,
                target.CaptureRect,
                target.Dpi,
            },
            Nano = new
            {
                Port = port,
                FirmwareVersion = session.ReadyInfo.FirmwareVersion.ToString(),
                Capabilities = $"0x{(ushort)session.ReadyInfo.Capabilities:X4}",
                Route = "NanoSerialHid",
                Fallback = "None",
                AltTabCount = altTabCount,
                FirstMove = firstMove,
                CorrectionMove = correctionMove,
                Click = "Left down/up; matching ACK per state",
                AllUp = true,
            },
            Before = before.ToEvidence(),
            ClickFrame = clickFrame.ToEvidence(),
            After = after.ToEvidence(),
            ExpectLabelAbsent = expectLabelAbsent,
            Input = new
            {
                DispatchRoute = "NanoSerialHid only",
                SendInputDispatchCount = 0,
                ComputerUseDispatchCount = 0,
                ExternalAiTransmissionCount = 0,
                ExternalAiApiCostUsd = 0,
            },
            Passed = passed,
        };
        var outputPath = Path.Combine(
            outputDirectory,
            $"live-discovery-nano-action-{DateTime.Now:yyyyMMdd-HHmmss-fff}.json");
        await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(evidence, JsonOptions));
        Console.WriteLine(outputPath);
        Console.WriteLine(passed ? "PASS" : "FAIL: expected outcome was not observed");
        return passed ? 0 : 3;
    }

    internal static WindowsOcrWord ValidatePinnedObservation(string path, string label)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var candidates = document.RootElement.GetProperty("GroundedCandidates").EnumerateArray()
            .Where(candidate => candidate.GetProperty("Status").GetString() == "Grounded"
                && candidate.GetProperty("Box").ValueKind == JsonValueKind.Object)
            .Select(candidate =>
            {
                var box = candidate.GetProperty("Box");
                var boxText = box.GetProperty("Text").GetString() ?? string.Empty;
                var candidateLabel = candidate.GetProperty("Label").GetString() ?? string.Empty;
                return new
                {
                    Box = new WindowsOcrWord(
                        boxText,
                        box.GetProperty("X").GetDouble(),
                        box.GetProperty("Y").GetDouble(),
                        box.GetProperty("Width").GetDouble(),
                        box.GetProperty("Height").GetDouble()),
                    Score = Math.Max(
                        FrameBoundLabelMatcher.Similarity(candidateLabel, label),
                        FrameBoundLabelMatcher.Similarity(boxText, label)),
                };
            })
            .Where(candidate => candidate.Score >= 0.85)
            .GroupBy(candidate => (
                candidate.Box.Text,
                candidate.Box.X,
                candidate.Box.Y,
                candidate.Box.Width,
                candidate.Box.Height))
            .Select(group => group.OrderByDescending(candidate => candidate.Score).First())
            .OrderByDescending(candidate => candidate.Score)
            .ToArray();
        if (candidates.Length == 0
            || (candidates.Length > 1 && candidates[0].Score - candidates[1].Score < 0.15))
        {
            throw new InvalidOperationException(
                $"pinned observationにGroundedなlabel '{label}' の一意な近似候補がありません。inputを送らず停止します。");
        }

        return candidates[0].Box;
    }

    internal static int FocusTargetWithNano(nint targetWindow, SerialHidEmitter emitter)
    {
        for (var attempt = 0; attempt <= 20; attempt++)
        {
            if (GetForegroundWindow() == targetWindow)
            {
                return attempt;
            }
            emitter.Emit([Down("Key:LAlt"), Down("Key:Tab")]);
            emitter.Emit([Up("Key:Tab"), Up("Key:LAlt")]);
            Thread.Sleep(250);
        }
        throw new InvalidOperationException("Nano Alt+Tabでtarget windowを前面化できませんでした。fallbackせず停止します。");
    }

    private static async Task<GroundedFrame> CaptureAndGroundAsync(
        LiveDiscoveryObserveSmoke.WindowTarget target,
        string label,
        string outputDirectory,
        string suffix,
        WindowsOcrWord? anchor)
    {
        using var source = WgcFrameSource.CreateForWindow(
            target.Window,
            $"window:live-discovery-nano:{target.ProcessId}");
        var frame = await LiveDiscoveryObserveSmoke.WaitForFrameAsync(source, TimeSpan.FromSeconds(10));
        var png = LiveDiscoveryObserveSmoke.EncodePng(frame);
        var cropX = 0;
        var cropY = 0;
        var cropWidth = frame.Width;
        var cropHeight = frame.Height;
        var ocrScale = 2d;
        LiveDiscoveryObserveSmoke.EncodedPng ocrPng;
        if (anchor is null)
        {
            ocrPng = LiveDiscoveryObserveSmoke.EncodePng(frame, scaleFactor: ocrScale);
        }
        else
        {
            cropX = Math.Max(0, checked((int)Math.Floor(anchor.X)) - 64);
            cropY = Math.Max(0, checked((int)Math.Floor(anchor.Y)) - 32);
            var right = Math.Min(frame.Width, checked((int)Math.Ceiling(anchor.X + anchor.Width)) + 64);
            var bottom = Math.Min(frame.Height, checked((int)Math.Ceiling(anchor.Y + anchor.Height)) + 32);
            cropWidth = right - cropX;
            cropHeight = bottom - cropY;
            ocrScale = 4;
            ocrPng = LiveDiscoveryObserveSmoke.EncodeRegion(
                frame,
                cropX,
                cropY,
                cropWidth,
                cropHeight,
                ocrScale);
        }
        var capturedAt = DateTimeOffset.UtcNow;
        var prefix = $"live-discovery-nano-{capturedAt:yyyyMMdd-HHmmss-fff}-{suffix}";
        var framePath = Path.Combine(outputDirectory, prefix + ".png");
        var ocrPath = Path.Combine(outputDirectory, prefix + $".ocr{ocrScale:F0}x.png");
        await File.WriteAllBytesAsync(framePath, png.Bytes);
        await File.WriteAllBytesAsync(ocrPath, ocrPng.Bytes);
        var ocr = await WindowsOcrSmoke.RecognizeImageAsync(ocrPath, coordinateScale: ocrScale);
        if (cropX != 0 || cropY != 0)
        {
            ocr = ocr with
            {
                Words = ocr.Words.Select(word => word with
                {
                    X = word.X + cropX,
                    Y = word.Y + cropY,
                }).ToArray(),
            };
        }
        var candidate = LiveDiscoveryObserveSmoke.Ground(label, ocr, anchor);
        FoundryVisionResult? trackingVision = null;
        var trackingVisionNonLoopback = false;
        if (candidate.Status != "Grounded" && anchor is not null)
        {
            var visionScale = Math.Min(2d, 640d / cropWidth);
            var visionPng = LiveDiscoveryObserveSmoke.EncodeRegion(
                frame, cropX, cropY, cropWidth, cropHeight, visionScale);
            var daemon = LiveDiscoveryObserveSmoke.ReadDaemonState();
            await using var observer = new OwnedTcpConnectionObserver(
                [Environment.ProcessId, daemon.ProcessId]);
            using var client = new FoundryLocalVisionClient(
                daemon.Endpoint,
                "qwen3-vl-2b-instruct-cuda-gpu:2",
                TimeSpan.FromSeconds(20));
            trackingVision = await client.ProposeLabelsAsync(visionPng.Bytes);
            trackingVisionNonLoopback = observer.HasNonLoopbackEstablished;
            if (trackingVision.Status == FoundryVisionStatus.Completed
                && trackingVision.Labels.Count(item => string.Equals(item, label, StringComparison.Ordinal)) == 1
                && !trackingVisionNonLoopback)
            {
                candidate = new LiveDiscoveryObserveSmoke.GroundedCandidate(
                    label,
                    "Grounded",
                    1,
                    anchor,
                    ocr.RecognizerLanguage,
                    "TrackedVlmExact",
                    1,
                    "same-frame anchored crop; exact local VLM label; pinned OCR geometry only");
            }
        }

        return new GroundedFrame(
            capturedAt,
            frame.Width,
            frame.Height,
            Convert.ToHexString(SHA256.HashData(png.Bytes)).ToLowerInvariant(),
            framePath,
            ocr.RecognizerLanguage,
            ocr.ElapsedMs,
            ocr.Text,
            candidate,
            trackingVision,
            trackingVisionNonLoopback);
    }

    private static async Task<GroundedFrame> WaitForOutcomeAsync(
        LiveDiscoveryObserveSmoke.WindowTarget target,
        string label,
        string outputDirectory,
        bool expectLabelAbsent,
        WindowsOcrWord? anchor,
        TimeSpan timeout)
    {
        var started = Stopwatch.StartNew();
        GroundedFrame? last = null;
        while (started.Elapsed < timeout)
        {
            await Task.Delay(300);
            last = await CaptureAndGroundAsync(target, label, outputDirectory, "after", anchor);
            if (!expectLabelAbsent || last.Candidate.Status != "Grounded")
            {
                return last;
            }
            anchor = last.Candidate.Box;
        }
        return last ?? throw new TimeoutException("click後frameを取得できませんでした。");
    }

    private static SerialHidCursorPoint MapToScreen(
        LiveDiscoveryObserveSmoke.WindowRectangle rect,
        int frameWidth,
        int frameHeight,
        WindowsOcrWord box)
    {
        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        return new SerialHidCursorPoint(
            checked((int)Math.Round(rect.Left + (box.X + box.Width / 2) * width / frameWidth)),
            checked((int)Math.Round(rect.Top + (box.Y + box.Height / 2) * height / frameHeight)));
    }

    private static bool Contains(
        GroundedFrame frame,
        LiveDiscoveryObserveSmoke.WindowRectangle rect,
        SerialHidCursorPoint point)
    {
        var box = frame.Candidate?.Box;
        if (box is null)
        {
            return false;
        }
        var leftTop = MapToScreen(rect, frame.FrameWidth, frame.FrameHeight, box with { Width = 0, Height = 0 });
        var rightBottom = MapToScreen(
            rect,
            frame.FrameWidth,
            frame.FrameHeight,
            box with { X = box.X + box.Width, Y = box.Y + box.Height, Width = 0, Height = 0 });
        return point.X >= leftTop.X && point.X <= rightBottom.X
            && point.Y >= leftTop.Y && point.Y <= rightBottom.Y;
    }

    private static void RequireGrounded(GroundedFrame frame, string stage)
    {
        if (frame.Candidate.Status != "Grounded" || frame.Candidate.Box is null)
        {
            throw new InvalidOperationException($"{stage}のfresh frameでtargetを一意groundingできません。inputを送らず停止します。");
        }
    }

    private static SerialHidEmitter AssertSerialEmitter(IOutputEmitter emitter) =>
        emitter as SerialHidEmitter
        ?? throw new InvalidOperationException("Nano resident sessionがSerialHidEmitterを返しませんでした。");

    private static MappedOutputEdge Down(string token) => new(token, PhysicalInputEdge.Down);
    private static MappedOutputEdge Up(string token) => new(token, PhysicalInputEdge.Up);

    private static string RequiredArgument(string[] arguments, string name)
    {
        var index = Array.IndexOf(arguments, name);
        if (index < 0 || index == arguments.Length - 1 || string.IsNullOrWhiteSpace(arguments[index + 1]))
        {
            throw new ArgumentException($"{name} が必要です。");
        }
        return arguments[index + 1];
    }

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    private sealed class Win32CursorOracle : ISerialHidCursorOracle
    {
        public SerialHidCursorPoint ReadCurrent()
        {
            if (!GetCursorPos(out var point))
            {
                throw new InvalidOperationException($"GetCursorPos failed: {Marshal.GetLastWin32Error()}");
            }
            return new SerialHidCursorPoint(point.X, point.Y);
        }

        public SerialHidCursorPoint ReadAfterDelta(SerialHidCursorPoint previous)
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(100);
            SerialHidCursorPoint current;
            do
            {
                Thread.Sleep(5);
                current = ReadCurrent();
            }
            while (current == previous && DateTime.UtcNow < deadline);
            return current;
        }
    }

    private sealed record GroundedFrame(
        DateTimeOffset CapturedAtUtc,
        int FrameWidth,
        int FrameHeight,
        string Sha256,
        string LocalPath,
        string OcrLanguage,
        long OcrElapsedMs,
        string OcrText,
        LiveDiscoveryObserveSmoke.GroundedCandidate Candidate,
        FoundryVisionResult? TrackingVision,
        bool TrackingVisionNonLoopback)
    {
        public object ToEvidence() => new
        {
            CapturedAtUtc,
            FrameWidth,
            FrameHeight,
            Sha256,
            LocalPath,
            OcrLanguage,
            OcrElapsedMs,
            OcrText,
            Candidate,
            TrackingVision,
            TrackingVisionNonLoopback,
        };
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };
}
