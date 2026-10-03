using System.IO;
using System.Text.Json;
using System.Threading.Channels;
using OpenLogicool.Contracts.Capture;
using OpenLogicool.Contracts.Perception;
using OpenLogicool.Contracts.Playbooks;

namespace OpenLogicool.Host;

/// <summary>明示的な記録区間の入力とWGC画像を保存する。取得中にAIを呼ばない。</summary>
public sealed class DemonstrationTimelineRecording(
    IProductGameFrameSource capture,
    IProductGameFrameEvidenceSink evidence,
    Func<DemonstrationScreenPoint, IReadOnlyList<double>?> normalize,
    Func<DemonstrationTimelineFrame, CancellationToken, ValueTask<ObservedScene>> analyze,
    string directory,
    TimeProvider? timeProvider = null) : IDemonstrationTimelineRecording
{
    private readonly TimeProvider time = timeProvider ?? TimeProvider.System;
    private readonly object sync = new();
    private readonly Channel<DemonstrationTimelineInput> inputs = Channel.CreateUnbounded<DemonstrationTimelineInput>();
    private readonly List<DemonstrationTimelineFrame> frames = [];
    private readonly List<DemonstrationTimelineInput> events = [];
    private readonly HashSet<string> held = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource capturing = new();
    private Task? frameTask;
    private Task? inputTask;
    private StreamWriter? frameJournal;
    private StreamWriter? inputJournal;
    private DemonstrationTimelineFrame? latest;
    private DemonstrationSessionDraft? session;
    private DemonstrationRecorderStatus state;
    private Exception? failure;
    private int completed;
    private int total;
    private long ignoredOutside;
    private long discarded;
    private bool accepting;
    private bool disposed;

    public async Task StartAsync(DemonstrationSessionDraft draft, CancellationToken cancellationToken = default)
    {
        if (session is not null) throw new InvalidOperationException("既に記録を開始しています。");
        session = draft;
        Directory.CreateDirectory(directory);
        // 原本の形式を明記し、解析結果のschema 0.3と混同しない。
        await File.WriteAllTextAsync(Path.Combine(directory, "timeline-session.json"),
            JsonSerializer.Serialize(new { SchemaVersion = "0.4.0", Session = draft }), cancellationToken);
        frameJournal = NewJournal("timeline-frames.jsonl");
        inputJournal = NewJournal("timeline-inputs.jsonl");
        await CaptureOneAsync(cancellationToken).ConfigureAwait(false);
        lock (sync) { accepting = true; state = DemonstrationRecorderStatus.Recording; }
        frameTask = CaptureLoopAsync();
        inputTask = SaveInputsAsync();
    }

    public void Observe(DemonstrationInputEdge edge)
    {
        // 座標だけその場で正規化する。画面取得、保存、解析はhook threadで行わない。
        IReadOnlyList<double>? point;
        try { point = edge.ScreenPoint is { } screen ? normalize(screen) : null; }
        catch (Exception exception)
        {
            // 対象windowの消失など、OS境界の失敗をhookから投げず記録のfaultとして通知する。
            lock (sync) { failure = exception; state = DemonstrationRecorderStatus.Fault; accepting = false; }
            return;
        }
        lock (sync)
        {
            if (!accepting || failure is not null || state == DemonstrationRecorderStatus.Paused) return;
            if (edge.ScreenPoint is not null && point is null) { ignoredOutside++; return; }
            var down = edge.Kind is DemonstrationInputEdgeKind.PointerDown or DemonstrationInputEdgeKind.KeyDown;
            if (down) held.Add(edge.ControlId);
            else if (edge.Kind is DemonstrationInputEdgeKind.PointerUp or DemonstrationInputEdgeKind.KeyUp)
            {
                if (held.Remove(edge.ControlId)) total++;
            }
            else total++;
            inputs.Writer.TryWrite(new DemonstrationTimelineInput(
                edge.OccurredUtc, latest!.Frame.Sequence, edge.Source, edge.Kind, edge.ControlId, edge.OutputToken,
                edge.MonotonicMs, point, edge.WheelVerticalSteps, edge.WheelHorizontalSteps, null));
        }
    }

    public void ObserveForeground(string? targetApplicationPath, DateTimeOffset occurredUtc)
    {
        lock (sync)
        {
            if (!accepting || failure is not null) return;
            var target = string.Equals(targetApplicationPath, session!.TargetApplicationPath, StringComparison.OrdinalIgnoreCase);
            var desired = target ? DemonstrationRecorderStatus.Recording : DemonstrationRecorderStatus.Paused;
            if (state == desired) return;
            state = desired;
            if (!target) { discarded += held.Count; held.Clear(); }
            inputs.Writer.TryWrite(new DemonstrationTimelineInput(
                occurredUtc, latest!.Frame.Sequence, null, null, null, null, 0, null, 0, 0, target));
        }
    }

    public DemonstrationRecordingStatus Status()
    {
        lock (sync) return new(state, session?.SessionId, held.Count, 0, ignoredOutside, 0, discarded,
            completed, total, failure?.Message);
    }

    public async Task<DemonstrationSessionRecord> StopAndAnalyzeAsync(
        IDemonstrationSessionStore store, CancellationToken cancellationToken = default)
    {
        DateTimeOffset stopped;
        lock (sync)
        {
            accepting = false;
            stopped = time.GetUtcNow();
            discarded += held.Count;
            held.Clear();
            state = DemonstrationRecorderStatus.Analyzing;
        }
        capturing.Cancel();
        inputs.Writer.TryComplete();
        await Task.WhenAll(frameTask!, inputTask!).ConfigureAwait(false);
        await frameJournal!.DisposeAsync();
        await inputJournal!.DisposeAsync();
        frameJournal = inputJournal = null;
        await File.WriteAllTextAsync(Path.Combine(directory, "timeline-stopped.json"),
            JsonSerializer.Serialize(new { SchemaVersion = "0.4.0", StoppedUtc = stopped, OperationCount = total }),
            cancellationToken).ConfigureAwait(false);
        if (failure is not null)
            throw new InvalidOperationException($"記録の取得・保存に失敗しました。原本は保持されています。{failure.Message}", failure);
        var result = await DemonstrationTimelineAnalyzer.AnalyzeAsync(
            session!, frames, events, stopped, store, analyze,
            value => { lock (sync) completed = value; }, cancellationToken).ConfigureAwait(false);
        lock (sync) state = DemonstrationRecorderStatus.Stopped;
        return result;
    }

    private StreamWriter NewJournal(string name) => new(new FileStream(
        Path.Combine(directory, name), FileMode.CreateNew, FileAccess.Write, FileShare.Read,
        4096, FileOptions.Asynchronous | FileOptions.WriteThrough));

    private async Task CaptureOneAsync(CancellationToken cancellationToken)
    {
        var frame = await capture.CaptureAsync(cancellationToken).ConfigureAwait(false);
        // 静止した窓ではWGCが新frameを出さない。画像は共有し、観測した時刻を別に残す。
        var observedUtc = time.GetUtcNow();
        var artifact = latest?.Frame.Sequence == frame.Sequence
            ? latest.Artifact : await evidence.SaveAsync(frame, cancellationToken).ConfigureAwait(false);
        var stored = new DemonstrationTimelineFrame(frame with { Pixels = null }, artifact, observedUtc);
        await frameJournal!.WriteLineAsync(JsonSerializer.Serialize(stored));
        await frameJournal.FlushAsync(cancellationToken);
        frames.Add(stored);
        lock (sync) latest = stored;
    }

    private async Task CaptureLoopAsync()
    {
        try
        {
            while (true)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250), time, capturing.Token).ConfigureAwait(false);
                await CaptureOneAsync(capturing.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (capturing.IsCancellationRequested) { }
        catch (Exception exception)
        {
            lock (sync) { failure = exception; state = DemonstrationRecorderStatus.Fault; accepting = false; }
        }
    }

    private async Task SaveInputsAsync()
    {
        try
        {
            await foreach (var input in inputs.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                await inputJournal!.WriteLineAsync(JsonSerializer.Serialize(input));
                await inputJournal.FlushAsync();
                events.Add(input);
            }
        }
        catch (Exception exception)
        {
            lock (sync) { failure = exception; state = DemonstrationRecorderStatus.Fault; accepting = false; }
            capturing.Cancel();
        }
    }

    public void Dispose()
    {
        lock (sync)
        {
            if (disposed) return;
            disposed = true;
            accepting = false;
        }
        capturing.Cancel();
        inputs.Writer.TryComplete();
        Task.WhenAll(frameTask ?? Task.CompletedTask, inputTask ?? Task.CompletedTask).GetAwaiter().GetResult();
        frameJournal?.Dispose();
        inputJournal?.Dispose();
        capturing.Dispose();
        (capture as IDisposable)?.Dispose();
    }
}

/// <summary>PNG原本へ束縛されたframe。pixel配列はjournalへ保存しない。</summary>
public sealed record DemonstrationTimelineFrame(CapturedFrame Frame, CapturedFrameArtifact Artifact, DateTimeOffset ObservedUtc)
{
    public DemonstrationTimelineFrame(CapturedFrame frame, CapturedFrameArtifact artifact) : this(frame, artifact, frame.WallClockUtc) { }
}

/// <summary>他appのpathとdesktop絶対座標を持たない、取得時点の入力。</summary>
public sealed record DemonstrationTimelineInput(
    DateTimeOffset OccurredUtc, long FrameSequence, DemonstrationInputSource? Source,
    DemonstrationInputEdgeKind? Kind, string? ControlId, string? OutputToken, double MonotonicMs,
    IReadOnlyList<double>? NormalizedPoint, int WheelVerticalSteps, int WheelHorizontalSteps,
    bool? TargetForeground);
