using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using OpenLogicool.Capture;
using OpenLogicool.Contracts.Capture;
using OpenLogicool.Contracts.Devices.Shared;
using OpenLogicool.Host;
using OpenLogicool.Input;

namespace OpenLogicool.Probe;

/// <summary>文字を持たないcontrolをfresh frame上の正規化座標とpatchへ束縛してNanoだけで操作する。</summary>
internal static class LiveDiscoveryNanoCoordinateSmoke
{
    private const int DefaultPatchRadius = 40;
    private static readonly TimeSpan PointerUnhoverSettleDelay = TimeSpan.FromMilliseconds(750);

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

    private static async Task<int> RunAsync(string[] arguments, string outputDirectory)
    {
        var port = RequiredArgument(arguments, "--port");
        var processName = RequiredArgument(arguments, "--process");
        var action = ParseAction(RequiredArgument(arguments, "--action"));
        var normalizedX = ParseNormalizedCoordinate(RequiredArgument(arguments, "--x"), "--x");
        var normalizedY = ParseNormalizedCoordinate(RequiredArgument(arguments, "--y"), "--y");
        var patchRadius = ParsePatchRadius(OptionalArgument(arguments, "--patch-radius"));
        var expectedPatchSha256 = OptionalArgument(arguments, "--expected-patch-sha256");
        var expectedPatchDHash = OptionalArgument(arguments, "--expected-patch-dhash");
        var maxDHashDistance = ParseMaxDHashDistance(OptionalArgument(arguments, "--max-dhash-distance"));
        if (action == CoordinateAction.Click
            && string.IsNullOrWhiteSpace(expectedPatchSha256)
            && string.IsNullOrWhiteSpace(expectedPatchDHash))
        {
            throw new ArgumentException("clickには--expected-patch-sha256または--expected-patch-dhashが必要です。");
        }
        ValidateExpectedPatch(expectedPatchSha256);
        var expectedDHashValue = ParseDHash(expectedPatchDHash);

        Directory.CreateDirectory(outputDirectory);
        var target = LiveDiscoveryObserveSmoke.FindWindow(processName);
        var before = await CaptureAsync(target, normalizedX, normalizedY, patchRadius, outputDirectory, "before-focus");

        var exchange = new ProbeSerialPortFrameExchange(port);
        using var residentSession = new SerialHidResidentOutputSession(
            exchange,
            new SerialHidSemanticVersion(1, 1, 0),
            TimeSpan.FromMilliseconds(300),
            TimeSpan.FromMilliseconds(50),
            SerialHidProtocolV1.AllCapabilities);
        residentSession.Start();
        residentSession.Protocol.SendAllUp();
        var emitter = residentSession.Emitter as SerialHidEmitter
            ?? throw new InvalidOperationException("Nano resident sessionがSerialHidEmitterを返しませんでした。");
        var altTabCount = LiveDiscoveryNanoActionSmoke.FocusTargetWithNano(target.Window, emitter);

        var pointer = new SerialHidRelativePointer(residentSession.Protocol, new Win32CursorOracle());
        var parkingPoint = MapToScreen(target.CaptureRect, 0.5, 0.5);
        var parkingMove = pointer.MoveTo(parkingPoint);
        await Task.Delay(PointerUnhoverSettleDelay);
        var fresh = await CaptureAsync(target, normalizedX, normalizedY, patchRadius, outputDirectory, "fresh-unoccluded");
        var dHashDistance = expectedDHashValue is null
            ? (int?)null
            : DHashDistance(expectedDHashValue.Value, ParseDHash(fresh.TargetPatchDHash)!.Value);
        var patchMatched = expectedPatchSha256 is not null
            ? string.Equals(expectedPatchSha256, fresh.TargetPatchSha256, StringComparison.OrdinalIgnoreCase)
            : expectedDHashValue is null || dHashDistance <= maxDHashDistance;
        if (!patchMatched)
        {
            throw new InvalidOperationException(
                $"fresh target patchがpinned patchと一致しません。" +
                $"expectedSha256={expectedPatchSha256} actualSha256={fresh.TargetPatchSha256} " +
                $"expectedDHash={expectedPatchDHash} actualDHash={fresh.TargetPatchDHash} distance={dHashDistance} max={maxDHashDistance}。clickせず停止します。");
        }

        var screenPoint = MapToScreen(target.CaptureRect, normalizedX, normalizedY);
        var move = pointer.MoveTo(screenPoint);
        var observedCursor = new Win32CursorOracle().ReadCurrent();
        if (!IsCursorAtTarget(screenPoint, observedCursor))
        {
            throw new InvalidOperationException(
                $"Nano cursor readbackがtargetと一致しません。expected={screenPoint} actual={observedCursor}");
        }

        await Task.Delay(250);
        var armed = await CaptureAsync(target, normalizedX, normalizedY, patchRadius, outputDirectory, "armed");

        FrameEvidence after;
        if (action == CoordinateAction.Click)
        {
            emitter.Emit([Down("Mouse:Left")]);
            emitter.Emit([Up("Mouse:Left")]);
            await Task.Delay(900);
            after = await CaptureAsync(target, normalizedX, normalizedY, patchRadius, outputDirectory, "after-click");
        }
        else
        {
            after = armed;
        }

        var screenChanged = !string.Equals(armed.FrameSha256, after.FrameSha256, StringComparison.Ordinal);
        var passed = IsCursorAtTarget(screenPoint, observedCursor)
            && patchMatched
            && (action == CoordinateAction.Move || screenChanged);
        var evidence = new
        {
            SchemaVersion = "1.0.0",
            Probe = "live-discovery-nano-coordinate",
            CapturedAtUtc = DateTimeOffset.UtcNow,
            Action = action.ToString(),
            Target = new
            {
                target.ProcessId,
                target.ProcessName,
                target.WindowTitle,
                target.CaptureRect,
                target.Dpi,
                NormalizedX = normalizedX,
                NormalizedY = normalizedY,
                ScreenPoint = screenPoint,
                PatchRadius = patchRadius,
                ExpectedPatchSha256 = expectedPatchSha256,
                ExpectedPatchDHash = expectedPatchDHash,
                DHashDistance = dHashDistance,
                MaxDHashDistance = maxDHashDistance,
                PatchMatched = patchMatched,
            },
            Nano = new
            {
                Port = port,
                FirmwareVersion = residentSession.Protocol.ReadyInfo.FirmwareVersion.ToString(),
                Capabilities = $"0x{(ushort)residentSession.Protocol.ReadyInfo.Capabilities:X4}",
                Route = "NanoSerialHid",
                Fallback = "None",
                AltTabCount = altTabCount,
                ParkingMove = parkingMove,
                Move = move,
                CursorReadback = observedCursor,
                Click = action == CoordinateAction.Click ? "Left down/up; matching ACK per state" : "None",
                AllUp = true,
            },
            Before = before,
            Fresh = fresh,
            Armed = armed,
            After = after,
            ScreenChanged = screenChanged,
            OutcomeRule = action == CoordinateAction.Click
                ? "cursorを退避したfresh target patchが一致してからNano clickし、full frameが変化した"
                : "Nano cursor readback matched fresh-frame normalized target",
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
            $"live-discovery-nano-coordinate-{DateTime.Now:yyyyMMdd-HHmmss-fff}.json");
        await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(evidence, JsonOptions));
        Console.WriteLine(outputPath);
        Console.WriteLine(passed ? "PASS" : "FAIL: coordinate action outcome was not observed");
        return passed ? 0 : 3;
    }

    internal static double ParseNormalizedCoordinate(string value, string argumentName)
    {
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            || !double.IsFinite(parsed)
            || parsed <= 0
            || parsed >= 1)
        {
            throw new ArgumentOutOfRangeException(argumentName, "正規化座標は0より大きく1より小さい有限値が必要です。");
        }
        return parsed;
    }

    internal static bool IsCursorAtTarget(SerialHidCursorPoint expected, SerialHidCursorPoint actual) =>
        Math.Abs(expected.X - actual.X) <= 2 && Math.Abs(expected.Y - actual.Y) <= 2;

    internal static int ParsePatchRadius(string? value)
    {
        if (value is null)
        {
            return DefaultPatchRadius;
        }
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            || parsed is < 4 or > 128)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "patch radiusは4以上128以下の整数が必要です。");
        }
        return parsed;
    }

    internal static int ParseMaxDHashDistance(string? value)
    {
        if (value is null)
        {
            return 10;
        }
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            || parsed is < 0 or > 16)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "dHash distanceは0以上16以下の整数が必要です。");
        }
        return parsed;
    }

    internal static ulong? ParseDHash(string? value)
    {
        if (value is null)
        {
            return null;
        }
        if (value.Length != 16
            || !ulong.TryParse(value, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var parsed))
        {
            throw new ArgumentException("expected patch dHashは16桁hexでなければなりません。", nameof(value));
        }
        return parsed;
    }

    internal static int DHashDistance(ulong left, ulong right) => BitOperations.PopCount(left ^ right);

    internal static void ValidateExpectedPatch(string? value)
    {
        if (value is null)
        {
            return;
        }
        if (value.Length != 64 || value.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException("expected patch SHA-256は64桁hexでなければなりません。", nameof(value));
        }
    }

    private static async Task<FrameEvidence> CaptureAsync(
        LiveDiscoveryObserveSmoke.WindowTarget target,
        double normalizedX,
        double normalizedY,
        int patchRadius,
        string outputDirectory,
        string suffix)
    {
        using var source = WgcFrameSource.CreateForWindow(
            target.Window,
            $"window:live-discovery-nano-coordinate:{target.ProcessId}");
        var frame = await LiveDiscoveryObserveSmoke.WaitForFrameAsync(source, TimeSpan.FromSeconds(10));
        var png = LiveDiscoveryObserveSmoke.EncodePng(frame);
        var capturedAt = DateTimeOffset.UtcNow;
        var path = Path.Combine(
            outputDirectory,
            $"live-discovery-nano-coordinate-{capturedAt:yyyyMMdd-HHmmss-fff}-{suffix}.png");
        await File.WriteAllBytesAsync(path, png.Bytes);
        return new FrameEvidence(
            capturedAt,
            frame.Width,
            frame.Height,
            Convert.ToHexString(SHA256.HashData(png.Bytes)).ToLowerInvariant(),
            HashTargetPatch(frame, normalizedX, normalizedY, patchRadius),
            HashTargetPatchDHash(frame, normalizedX, normalizedY, patchRadius),
            path);
    }

    private static string HashTargetPatch(
        CapturedFrame frame,
        double normalizedX,
        double normalizedY,
        int radius)
    {
        var pixels = frame.Pixels
            ?? throw new InvalidOperationException("capture frameにpixel payloadがありません。");
        var centerX = checked((int)Math.Round(normalizedX * (frame.Width - 1)));
        var centerY = checked((int)Math.Round(normalizedY * (frame.Height - 1)));
        var left = Math.Max(0, centerX - radius);
        var top = Math.Max(0, centerY - radius);
        var right = Math.Min(frame.Width - 1, centerX + radius);
        var bottom = Math.Min(frame.Height - 1, centerY + radius);
        var rowBytes = checked((right - left + 1) * 4);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        for (var y = top; y <= bottom; y++)
        {
            hash.AppendData(pixels.Bgra8.Span.Slice(y * pixels.Stride + left * 4, rowBytes));
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static string HashTargetPatchDHash(
        CapturedFrame frame,
        double normalizedX,
        double normalizedY,
        int radius)
    {
        var pixels = frame.Pixels
            ?? throw new InvalidOperationException("capture frameにpixel payloadがありません。");
        var centerX = checked((int)Math.Round(normalizedX * (frame.Width - 1)));
        var centerY = checked((int)Math.Round(normalizedY * (frame.Height - 1)));
        var left = Math.Max(0, centerX - radius);
        var top = Math.Max(0, centerY - radius);
        var right = Math.Min(frame.Width - 1, centerX + radius);
        var bottom = Math.Min(frame.Height - 1, centerY + radius);
        ulong hash = 0;
        var bit = 0;
        for (var sampleY = 0; sampleY < 8; sampleY++)
        {
            var y = top + (int)Math.Round(sampleY * (bottom - top) / 7d);
            for (var sampleX = 0; sampleX < 8; sampleX++)
            {
                var x1 = left + (int)Math.Round(sampleX * (right - left) / 8d);
                var x2 = left + (int)Math.Round((sampleX + 1) * (right - left) / 8d);
                if (Luminance(pixels, x1, y) > Luminance(pixels, x2, y))
                {
                    hash |= 1UL << bit;
                }
                bit++;
            }
        }
        return hash.ToString("x16", CultureInfo.InvariantCulture);
    }

    private static int Luminance(FramePixels pixels, int x, int y)
    {
        var offset = y * pixels.Stride + x * 4;
        var span = pixels.Bgra8.Span;
        return span[offset] * 29 + span[offset + 1] * 150 + span[offset + 2] * 77;
    }

    internal static SerialHidCursorPoint MapToScreen(
        LiveDiscoveryObserveSmoke.WindowRectangle rect,
        double normalizedX,
        double normalizedY) =>
        new(
            checked((int)Math.Round(rect.Left + normalizedX * (rect.Right - rect.Left - 1))),
            checked((int)Math.Round(rect.Top + normalizedY * (rect.Bottom - rect.Top - 1))));

    private static CoordinateAction ParseAction(string value) => value.ToLowerInvariant() switch
    {
        "move" => CoordinateAction.Move,
        "click" => CoordinateAction.Click,
        _ => throw new ArgumentException("--actionはmoveまたはclickです。", nameof(value)),
    };

    private static string RequiredArgument(string[] arguments, string name) =>
        OptionalArgument(arguments, name)
        ?? throw new ArgumentException($"{name} が必要です。");

    private static string? OptionalArgument(string[] arguments, string name)
    {
        var index = Array.IndexOf(arguments, name);
        return index >= 0 && index < arguments.Length - 1 && !string.IsNullOrWhiteSpace(arguments[index + 1])
            ? arguments[index + 1]
            : null;
    }

    private static MappedOutputEdge Down(string token) => new(token, PhysicalInputEdge.Down);
    private static MappedOutputEdge Up(string token) => new(token, PhysicalInputEdge.Up);

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

    internal enum CoordinateAction
    {
        Move,
        Click,
    }

    private sealed record FrameEvidence(
        DateTimeOffset CapturedAtUtc,
        int Width,
        int Height,
        string FrameSha256,
        string TargetPatchSha256,
        string TargetPatchDHash,
        string LocalPath);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };
}
