using System.Diagnostics;
using System.Runtime.InteropServices;
using OpenLogicool.Capture;
using OpenLogicool.Contracts.Capture;
using OpenLogicool.Contracts.Devices.Shared;
using OpenLogicool.Contracts.Exploration;
using OpenLogicool.Contracts.Perception;
using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Desktop;
using OpenLogicool.Exploration;
using OpenLogicool.Input;
using OpenLogicool.Perception;
using OpenLogicool.Playbooks;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Security.Cryptography;

namespace OpenLogicool.Host;

public sealed record SupervisedWindowBounds(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
}

public interface ISupervisedWindowSession : IDisposable
{
    string SourceId { get; }
    SupervisedWindowBounds CaptureBounds { get; }
    CapturedFrame Capture(
        TimeSpan timeout,
        CancellationToken cancellationToken = default);
    SupervisedWindowBounds GetCaptureBounds(long transformRevision);
    bool Activate();
}

public interface ISupervisedWindowLocator
{
    ISupervisedWindowSession Locate(LearnedSceneProfileDocument profile);
}

public interface ISupervisedOcrReader
{
    OcrFrameSnapshot Recognize(CapturedFrame frame);
}

public interface ISupervisedNanoSession : IDisposable
{
    GameInteractionDispatchReceipt Dispatch(
        string primitive,
        AffordanceCandidate? target,
        SupervisedWindowBounds captureBounds,
        ObservedScene beforeScene);
}

public interface ISupervisedNanoSessionFactory
{
    ISupervisedNanoSession Open();
}

/// <summary>WGC/OCRでfresh sceneを同定し、確認済みstepだけをNanoへ一回渡す製品runtime。</summary>
public sealed class ProductSupervisedMacroRuntime(
    ILearnedSceneProfileStore profiles,
    ISupervisedWindowLocator windows,
    ISupervisedOcrReader ocr,
    ISupervisedNanoSessionFactory nanoFactory,
    Action<ObservedScene>? sceneObserver = null,
    Action<SupervisedMacroTransitionObservation>? transitionObserver = null) : ISupervisedMacroRuntimePort, IDisposable
{
    private LearnedSceneProfileDocument? profile;
    private VisualMacroProgram? program;
    private ISupervisedWindowSession? window;
    private ISupervisedNanoSession? nano;
    private SupervisedObservationRuntime? observation;

    public void Pin(VisualMacroProgram value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var loaded = profiles.Load(value.GameId, value.EnvironmentScope)
            ?? throw new InvalidOperationException("選択中のゲーム環境に学習済みscene profileがありません。");
        LearnedSceneProfileValidator.Validate(loaded);
        var stateIds = value.Steps.SelectMany(step => new[] { step.SourceStateId, step.DestinationStateId })
            .Distinct(StringComparer.Ordinal).ToArray();
        if (stateIds.Any(stateId => loaded.States.All(state => state.StateId != stateId)))
        {
            throw new InvalidOperationException("Visual Macroの全stateを照合できるscene signatureがありません。");
        }
        foreach (var step in value.Steps.Where(step => IsClick(step.Primitive)))
        {
            var source = loaded.States.Single(state => state.StateId == step.SourceStateId);
            if (!source.Affordances.Any(item => item.CandidateId == step.AffordanceCandidateId
                && item.AllowedPrimitives.Contains(step.Primitive, StringComparer.Ordinal)))
            {
                throw new InvalidOperationException($"step {step.Sequence} のfresh target再束縛dataがありません。");
            }
        }

        nano?.Dispose();
        window?.Dispose();
        nano = null;
        window = null;
        profile = null;
        program = null;
        observation = null;

        var openedWindow = windows.Locate(loaded);
        try
        {
            var openedNano = nanoFactory.Open();
            window = openedWindow;
            nano = openedNano;
            profile = loaded;
            program = value;
            observation = new SupervisedObservationRuntime(
                openedWindow,
                ocr,
                loaded,
                sceneObserver);
        }
        catch
        {
            openedWindow.Dispose();
            throw;
        }
    }

    public ObservedScene ObserveBefore(VisualMacroStep step)
    {
        EnsurePinned(step);
        return observation!.ObserveBefore(step);
    }

    public void DispatchNano(VisualMacroStep step, ObservedScene beforeScene)
    {
        EnsurePinned(step);
        if (beforeScene.CaptureAvailability != CaptureAvailability.Available
            || beforeScene.StateIdentity != StateIdentityStatus.Known
            || beforeScene.StateHypothesisId != step.SourceStateId
            || beforeScene.Frame.SourceId != window!.SourceId)
        {
            throw new SupervisedMacroDispatchNotStartedException(
                "Confirmedなfresh source sceneではないためNano入力を送りません。");
        }
        if (!window.Activate())
        {
            throw new SupervisedMacroDispatchNotStartedException(
                "対象ゲームwindowを前面化できないためNano入力を送りません。");
        }
        AffordanceCandidate? target = null;
        if (IsClick(step.Primitive))
        {
            target = beforeScene.Affordances.SingleOrDefault(candidate =>
                candidate.CandidateId == step.AffordanceCandidateId
                && candidate.AllowedPrimitives.Contains(step.Primitive, StringComparer.Ordinal))
                ?? throw new SupervisedMacroDispatchNotStartedException(
                    "fresh frameでclick targetを一意に再束縛できません。");
        }
        var receipt = nano!.Dispatch(
            step.Primitive,
            target,
            window.GetCaptureBounds(beforeScene.Frame.TransformRevision),
            beforeScene);
        if (receipt.Status != GameInteractionDispatchStatus.Dispatched)
        {
            throw new InvalidOperationException(receipt.FailureReason ?? "Nano dispatch failed");
        }
    }

    public SupervisedMacroTransitionObservation ObserveAfter(
        VisualMacroStep step,
        ObservedScene beforeScene)
    {
        EnsurePinned(step);
        var stability = new GameInteractionStabilityRuntime(
                observation!,
                new SystemGameInteractionClock(),
                TimeSpan.FromMilliseconds(100))
            .WaitStableAsync(beforeScene, step.WaitCondition)
            .AsTask().GetAwaiter().GetResult();
        var comparison = new GameTransitionJudge().Compare(beforeScene, stability);
        var final = stability.StableScene ?? stability.Observations.LastOrDefault();
        if (final is null)
        {
            throw new InvalidOperationException("操作後の画面を1件も観測できませんでした。");
        }
        var result = new SupervisedMacroTransitionObservation(
            stability,
            comparison,
            final,
            comparison.Judgement == GameTransitionJudgement.Moved
                && string.Equals(final.StateHypothesisId, step.DestinationStateId, StringComparison.Ordinal));
        transitionObserver?.Invoke(result);
        return result;
    }

    public void Dispose()
    {
        nano?.Dispose();
        window?.Dispose();
    }

    private void EnsurePinned(VisualMacroStep step)
    {
        if (profile is null || program is null || window is null || nano is null
            || !program.Steps.Any(item => ReferenceEquals(item, step) || item == step))
        {
            throw new InvalidOperationException("Visual Macro runtimeがこのprogramへpinされていません。");
        }
    }

    private static bool IsClick(string primitive) => primitive is "click" or "frame-bound pointer click";

    private sealed class SupervisedObservationRuntime(
        ISupervisedWindowSession window,
        ISupervisedOcrReader ocr,
        LearnedSceneProfileDocument profile,
        Action<ObservedScene>? observer) : IGameObservationRuntime
    {
        private readonly Dictionary<string, ObservedScene> scenes = new(StringComparer.Ordinal);

        public ObservedScene ObserveBefore(VisualMacroStep step)
        {
            var timeout = TimeSpan.FromMilliseconds(step.WaitCondition.TimeoutMilliseconds);
            var started = Stopwatch.StartNew();
            ObservedScene? last = null;
            while (started.Elapsed < timeout)
            {
                var remaining = timeout - started.Elapsed;
                last = ObserveScene(remaining);
                if (last.CaptureAvailability == CaptureAvailability.Available
                    && last.StateIdentity == StateIdentityStatus.Known)
                {
                    if (!string.Equals(last.StateHypothesisId, step.SourceStateId, StringComparison.Ordinal))
                    {
                        return last;
                    }
                    if (!IsClick(step.Primitive)
                        || VisualMacroAuditor.HasRequiredTarget(step, last))
                    {
                        return last;
                    }
                }
                var remainingAfterObservation = timeout - started.Elapsed;
                if (remainingAfterObservation <= TimeSpan.Zero)
                {
                    break;
                }
                Thread.Sleep(checked((int)Math.Min(100, remainingAfterObservation.TotalMilliseconds)));
            }
            return last ?? throw new TimeoutException("対象windowのsceneを観測できないままtimeoutしました。");
        }

        public ObservedScene ObserveScene(
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            var frame = window.Capture(timeout, cancellationToken);
            var scene = LearnedSceneMatcher.Match(profile, frame, ocr.Recognize(frame));
            scenes[scene.ObservationId] = scene;
            observer?.Invoke(scene);
            return scene;
        }

        public ValueTask<ObservationResult> ObserveAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var scene = ObserveScene(TimeSpan.FromSeconds(1), cancellationToken);
            return ValueTask.FromResult(new ObservationResult(
                scene.SchemaVersion,
                scene.ObservationId,
                scene.Frame,
                scene.CaptureAvailability,
                scene.StateIdentity,
                scene.StateCandidates,
                scene.PerceptionVersion,
                scene.Frame.FreshnessMs,
                null));
        }

        public ValueTask<ObservedScene> DiscoverTargetsAsync(
            ObservationResult current,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return scenes.TryGetValue(current.ObservationId, out var scene)
                ? ValueTask.FromResult(scene)
                : throw new InvalidOperationException("Observeしていないsceneは解決できません。");
        }
    }
}

public sealed class WindowsSupervisedWindowLocator : ISupervisedWindowLocator
{
    public ISupervisedWindowSession Locate(LearnedSceneProfileDocument profile)
    {
        LearnedSceneProfileValidator.Validate(profile);
        var matches = Process.GetProcessesByName(profile.ProcessName)
            .Where(process => process.MainWindowHandle != IntPtr.Zero
                && (profile.WindowTitle is null || process.MainWindowTitle == profile.WindowTitle))
            .ToArray();
        if (matches.Length != 1)
        {
            foreach (var process in matches)
            {
                process.Dispose();
            }
            throw new InvalidOperationException(
                $"対象window '{profile.ProcessName}' は{matches.Length}件です。一意な実行中windowが必要です。");
        }
        return new WindowsSupervisedWindowSession(matches[0]);
    }

    private sealed class WindowsSupervisedWindowSession : ISupervisedWindowSession
    {
        private readonly Process process;
        private readonly nint handle;
        private SupervisedWindowBounds currentBounds;
        private long transformRevision = 1;

        public WindowsSupervisedWindowSession(Process process)
        {
            this.process = process;
            handle = process.MainWindowHandle;
            currentBounds = ReadBounds(handle);
            SourceId = $"window:supervised:{process.Id}";
        }

        public string SourceId { get; }
        public SupervisedWindowBounds CaptureBounds => currentBounds;

        public CapturedFrame Capture(
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            RefreshBounds();
            using var source = WgcFrameSource.CreateForWindow(handle, SourceId, includeCursor: false);
            var started = Stopwatch.StartNew();
            while (started.Elapsed < timeout)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var read = source.PullDetailed();
                if (read.Fault is not null)
                {
                    throw new InvalidOperationException(
                        $"WGC capture fault: {read.Fault.Kind}: {read.Fault.Detail}");
                }
                if (read.Result is FrameAvailable available)
                {
                    return available.Frame with { TransformRevision = transformRevision };
                }
                Thread.Sleep(20);
            }
            throw new TimeoutException("対象windowのfresh WGC frameが到着しませんでした。");
        }

        public SupervisedWindowBounds GetCaptureBounds(long expectedTransformRevision)
        {
            RefreshBounds();
            if (expectedTransformRevision != transformRevision)
            {
                throw new SupervisedMacroDispatchNotStartedException(
                    "操作前frameの後にwindow位置またはsizeが変わったため入力を送りません。");
            }
            return currentBounds;
        }

        public bool Activate() => SetForegroundWindow(handle) && GetForegroundWindow() == handle;

        public void Dispose() => process.Dispose();

        private void RefreshBounds()
        {
            var latest = ReadBounds(handle);
            if (latest != currentBounds)
            {
                currentBounds = latest;
                transformRevision = checked(transformRevision + 1);
            }
        }

        private static SupervisedWindowBounds ReadBounds(nint window)
        {
            const int DwmwaExtendedFrameBounds = 9;
            var result = DwmGetWindowAttribute(window, DwmwaExtendedFrameBounds, out var rect, Marshal.SizeOf<NativeRect>());
            if (result != 0 || rect.Right <= rect.Left || rect.Bottom <= rect.Top)
            {
                throw new InvalidOperationException($"対象windowのcapture boundsを取得できません: 0x{result:X8}");
            }
            return new SupervisedWindowBounds(rect.Left, rect.Top, rect.Right, rect.Bottom);
        }
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();
    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(nint window, int attribute, out NativeRect value, int valueSize);
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left; public int Top; public int Right; public int Bottom; }
}

public sealed class WindowsOcrFrameReader(Action<OcrFrameSnapshot>? observer = null) : ISupervisedOcrReader
{
    public OcrFrameSnapshot Recognize(CapturedFrame frame)
    {
        var pixels = frame.Pixels ?? throw new InvalidOperationException("OCRにはBGRA8 frame pixelsが必要です。");
        var source = System.Windows.Media.Imaging.BitmapSource.Create(
            frame.Width, frame.Height, frame.DpiX, frame.DpiY,
            System.Windows.Media.PixelFormats.Bgra32, null, pixels.Bgra8.ToArray(), pixels.Stride);
        var scaled = new System.Windows.Media.Imaging.TransformedBitmap(
            source, new System.Windows.Media.ScaleTransform(2, 2));
        System.Windows.Media.RenderOptions.SetBitmapScalingMode(scaled, System.Windows.Media.BitmapScalingMode.HighQuality);
        var stride = checked(scaled.PixelWidth * 4);
        var buffer = new byte[checked(stride * scaled.PixelHeight)];
        scaled.CopyPixels(buffer, stride, 0);
        using var bitmap = SoftwareBitmap.CreateCopyFromBuffer(
            CryptographicBuffer.CreateFromByteArray(buffer), BitmapPixelFormat.Bgra8,
            scaled.PixelWidth, scaled.PixelHeight, BitmapAlphaMode.Ignore);
        var engine = OcrEngine.TryCreateFromUserProfileLanguages()
            ?? throw new InvalidOperationException("Windows OCR engineを利用できません。");
        var result = engine.RecognizeAsync(bitmap).AsTask().GetAwaiter().GetResult();
        var words = result.Lines.SelectMany(line => line.Words).Select(word => new OcrWordBox(
            word.Text,
            word.BoundingRect.X / 2,
            word.BoundingRect.Y / 2,
            word.BoundingRect.Width / 2,
            word.BoundingRect.Height / 2)).ToArray();
        var snapshot = new OcrFrameSnapshot("Windows.Media.Ocr:v1", engine.RecognizerLanguage.LanguageTag, words);
        observer?.Invoke(snapshot);
        return snapshot;
    }
}

public sealed class SerialHidSupervisedNanoSessionFactory(
    SerialHidDiscoveryService discovery,
    string? selectedDeviceInstanceId) : ISupervisedNanoSessionFactory
{
    public ISupervisedNanoSession Open()
    {
        var selection = discovery.Resolve(selectedDeviceInstanceId, SerialHidProtocolV1.AllCapabilities);
        try
        {
            selection.Session.Start();
            return new Session(selection.Session);
        }
        catch
        {
            selection.Session.Dispose();
            throw;
        }
    }

    private sealed class Session(SerialHidResidentOutputSession resident) : ISupervisedNanoSession
    {
        public GameInteractionDispatchReceipt Dispatch(
            string primitive,
            AffordanceCandidate? target,
            SupervisedWindowBounds bounds,
            ObservedScene beforeScene)
        {
            var emitter = resident.Emitter as SerialHidEmitter
                ?? throw new InvalidOperationException("Nano sessionがSerial HID emitterではありません。");
            var observation = new ObservationResult(
                beforeScene.SchemaVersion,
                beforeScene.ObservationId,
                beforeScene.Frame,
                beforeScene.CaptureAvailability,
                beforeScene.StateIdentity,
                beforeScene.StateCandidates,
                beforeScene.PerceptionVersion,
                beforeScene.Frame.FreshnessMs,
                null);
            var actions = new NanoGameInteractionActions(
                new SerialHidNanoGameInputDevice(resident.Protocol, emitter, new Win32CursorOracle()),
                new WindowsGameInteractionCoordinateMapper(() => new GameCaptureScreenBounds(
                    bounds.Left,
                    bounds.Top,
                    bounds.Width,
                    bounds.Height)));
            if (primitive is "Escape" or "escape" or "back")
            {
                return actions.KeyTap(new GameInteractionKeyTapRequest(
                    beforeScene.SchemaVersion,
                    beforeScene.ObservationId,
                    beforeScene.Frame.Sequence,
                    beforeScene.Frame.TransformRevision,
                    beforeScene.Frame.SourceId,
                    ["Key:Esc"]), observation);
            }
            if (primitive is not ("click" or "frame-bound pointer click") || target is null)
            {
                throw new InvalidOperationException($"Nano primitive '{primitive}' は教師付きruntimeで未対応です。");
            }
            return actions.Click(GameInteractionTargetBinding.From(target), observation);
        }

        public void Dispose() => resident.Dispose();
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
            var current = ReadCurrent();
            while (current == previous && DateTime.UtcNow < deadline)
            {
                Thread.Sleep(5);
                current = ReadCurrent();
            }
            return current;
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetCursorPos(out NativePoint point);
    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X; public int Y; }
}
