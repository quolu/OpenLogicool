using System.Collections.Concurrent;
using System.Diagnostics;
using OpenLogicool.Contracts.Devices.Shared;
using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Domain;

namespace OpenLogicool.Input;

/// <summary>fast path の fault（計画 §6.2: queue overflow・未知 device 等では drop 継続せず fault 停止する）。</summary>
public sealed class FastPathFaultException(string message) : Exception(message);

/// <summary>fast path の入力面: input source と、その drop 計数の観測点（source が公開している場合）。</summary>
public sealed record FastPathSource(
    IDeviceInputSource Source,
    Func<long>? DroppedCountProbe = null);

/// <summary>
/// Device Input → Mapping Runtime → Input Emitter を繋ぐ fast path の実行 loop（計画 §6.1〜6.2）。
/// 専用 worker thread が各 source を非 blocking で pull し、device instance ごとの
/// DeviceMappingRuntime で解決して emitter へ送る。AI・network・capture・SQLite・UI を一切待たない。
///
/// fault 方針（§6.2）: source の drop 検出・未知 device instance・emitter fault では
/// 継続せず停止し、全 runtime の所有 output を release して Failure に原因を保持する。
/// </summary>
public sealed class FastPathPump : IDisposable
{
    private readonly IReadOnlyList<FastPathSource> _sources;
    private readonly IReadOnlyDictionary<string, DeviceMappingRuntime> _runtimes;
    private readonly IOutputEmitter _emitter;
    private readonly IMacroInvocationSink? _macroInvocations;
    private readonly ConcurrentQueue<(string DeviceInstanceId, MappingProfile Profile)> _profileChangeRequests = new();
    private readonly Thread _worker;
    private readonly AutoResetEvent _controlWake = new(false);
    private readonly WaitHandle[] _wakeHandles;
    private readonly bool _allSourcesSignal;
    private readonly bool _traceEnabled;
    private readonly int _traceCapacity;
    private readonly ConcurrentQueue<InputTraceEntry> _traceBuffer = new();
    private volatile bool _stopRequested;
    private volatile bool _started;
    private Exception? _failure;
    private long _processedCount;
    private long _traceSequence;
    private long _traceApproxCount;
    private long _acceptedMacroInvocations;
    private long _rejectedMacroInvocations;
    private MacroInvocationEnqueueResult? _lastMacroRejection;
    private bool _disposed;

    /// <summary>
    /// trace（test field・Journey A-6）は既定 off。有効化すると worker が処理した各 input を
    /// 非 blocking で有界 buffer（既定 256 件・drop-oldest）へ enqueue する。in-memory enqueue は
    /// 計画 §6.1 の「AI・network・capture・SQLite・UI を待たない」の禁止対象ではない（待機を作らない限り）。
    /// </summary>
    public FastPathPump(
        IReadOnlyList<FastPathSource> sources,
        IReadOnlyDictionary<string, DeviceMappingRuntime> runtimesByDeviceInstanceId,
        IOutputEmitter emitter,
        bool enableTrace = false,
        int traceCapacity = 256,
        IMacroInvocationSink? macroInvocations = null)
    {
        _sources = sources;
        _runtimes = runtimesByDeviceInstanceId;
        _emitter = emitter;
        _macroInvocations = macroInvocations;
        _traceEnabled = enableTrace;
        _traceCapacity = traceCapacity;
        var sourceSignals = sources
            .Select(entry => (entry.Source as IDeviceInputSignalSource)?.InputAvailable)
            .Where(signal => signal is not null)
            .Cast<WaitHandle>()
            .ToArray();
        _allSourcesSignal = sourceSignals.Length == sources.Count;
        _wakeHandles = [_controlWake, .. sourceSignals];
        _worker = new Thread(Worker) { IsBackground = true, Name = "OpenLogicoolFastPath" };
    }

    /// <summary>worker が fault 停止した原因（null なら正常）。</summary>
    public Exception? Failure => _failure;

    public bool IsRunning => _started && !_stopRequested && _failure is null && _worker.IsAlive;

    /// <summary>処理済み PhysicalInput 件数。</summary>
    public long ProcessedCount => Interlocked.Read(ref _processedCount);

    public long AcceptedMacroInvocations => Interlocked.Read(ref _acceptedMacroInvocations);

    public long RejectedMacroInvocations => Interlocked.Read(ref _rejectedMacroInvocations);

    public MacroInvocationEnqueueResult? LastMacroRejection => _lastMacroRejection;

    public void Start()
    {
        if (_started)
        {
            throw new InvalidOperationException("fast path pump は既に開始されています。");
        }

        _started = true;
        _worker.Start();
    }

    /// <summary>
    /// 全 source を一巡 pull して処理する（worker loop の1反復。テストからは同期で呼べる）。
    /// 返り値は処理した input 件数。
    /// </summary>
    /// <summary>
    /// foreground app 切替等による profile 差し替えを依頼する（任意 thread から可）。
    /// 適用は worker の次の RunOnce 冒頭で行われ、変更は新規 down から有効（DEV-007・MAP-010:
    /// runtime mapping の差し替えのみで device write はしない）。
    /// </summary>
    public void RequestProfileChange(string deviceInstanceId, MappingProfile profile)
    {
        _profileChangeRequests.Enqueue((deviceInstanceId, profile));
        _controlWake.Set();
    }

    public int RunOnce()
    {
        var processed = 0;
        while (_profileChangeRequests.TryDequeue(out var request))
        {
            if (!_runtimes.TryGetValue(request.DeviceInstanceId, out var runtime))
            {
                throw new FastPathFaultException(
                    $"profile 変更対象の device instance '{request.DeviceInstanceId}' の Mapping Runtime が構成されていません。");
            }

            runtime.ApplyProfile(request.Profile);
        }

        foreach (var entry in _sources)
        {
            if (entry.DroppedCountProbe is not null)
            {
                var dropped = entry.DroppedCountProbe();
                if (dropped > 0)
                {
                    throw new FastPathFaultException(
                        $"input source が {dropped} 件を drop しました。fast path は drop して継続しない（§6.2）。");
                }
            }

            while (entry.Source.TryPull(out var input))
            {
                if (!_runtimes.TryGetValue(input.DeviceInstanceId, out var runtime))
                {
                    throw new FastPathFaultException(
                        $"device instance '{input.DeviceInstanceId}' の Mapping Runtime が構成されていません。");
                }

                var layerId = runtime.CurrentLayerId;
                var edges = runtime.Process(input);
                var emitted = Dispatch(edges);
                RecordTrace(input, layerId, edges, emitted, MonotonicMilliseconds());
                processed++;
                Interlocked.Increment(ref _processedCount);
            }

            if (entry.Source is IDeviceChangeSource changeSource)
            {
                while (changeSource.TryPullDeviceChange(out var change))
                {
                    ProcessDeviceChange(change);
                }
            }
        }

        return processed;
    }

    private bool Dispatch(IReadOnlyList<MappedOutputEdge> edges)
    {
        if (edges.Count == 0)
        {
            return false;
        }

        List<MappedOutputEdge>? physical = null;
        var dispatched = false;
        foreach (var edge in edges)
        {
            if (!MacroInvocationTokens.IsMacro(edge.Output))
            {
                (physical ??= []).Add(edge);
                continue;
            }

            if (edge.Edge != PhysicalInputEdge.Down)
            {
                throw new FastPathFaultException("macro invocationはbutton downでだけ起動できます。");
            }

            var result = _macroInvocations?.TryEnqueue(MacroInvocationTokens.Parse(edge.Output))
                ?? MacroInvocationEnqueueResult.Unavailable;
            if (result == MacroInvocationEnqueueResult.Accepted)
            {
                Interlocked.Increment(ref _acceptedMacroInvocations);
                dispatched = true;
            }
            else
            {
                _lastMacroRejection = result;
                Interlocked.Increment(ref _rejectedMacroInvocations);
            }
        }

        if (physical is { Count: > 0 })
        {
            _emitter.Emit(physical);
            dispatched = true;
        }
        return dispatched;
    }

    /// <summary>
    /// device の切断・再接続を処理する（DEV-008: 切断は新規 down を止めて所有 output を release、
    /// 再接続は新規 down の受理を再開する）。runtime 未構成の device の change は所有 output が
    /// 存在しないため対象外（その device の input は既存の未知 device fault で検出される）。
    /// </summary>
    private void ProcessDeviceChange(DeviceChange change)
    {
        if (!_runtimes.TryGetValue(change.DeviceInstanceId, out var runtime))
        {
            return;
        }

        if (change.Kind == DeviceChangeKind.Removal && runtime.AcceptsNewDowns)
        {
            _emitter.Emit(runtime.StopAndReleaseAll());
        }
        else if (change.Kind == DeviceChangeKind.Arrival && !runtime.AcceptsNewDowns)
        {
            runtime.Resume();
        }
    }

    /// <summary>
    /// 停止（DEV-008: 新規 down を止めてから所有 output を release）。
    /// worker を終えてから全 runtime を StopAndReleaseAll し、release を実送出する。
    /// </summary>
    public void Stop()
    {
        _stopRequested = true;
        _controlWake.Set();
        if (_started && _worker.IsAlive)
        {
            if (!_worker.Join(TimeSpan.FromSeconds(5)))
            {
                throw new InvalidOperationException("fast path worker が 5 秒以内に停止しませんでした。");
            }
        }

        if (_failure is null)
        {
            ReleaseAllOwnedOutputs();
        }
    }

    private void Worker()
    {
        try
        {
            while (!_stopRequested)
            {
                if (RunOnce() == 0)
                {
                    // live sourceはqueue投入後にsignalするため、WindowsのSleep(1)量子へ依存せず即時起床する。
                    // signal非対応sourceだけは既存互換の短周期pullを維持する。
                    WaitHandle.WaitAny(_wakeHandles, _allSourcesSignal ? 50 : 1);
                }
            }
        }
        catch (Exception ex)
        {
            _failure = ex;
            try
            {
                ReleaseAllOwnedOutputs();
            }
            catch (Exception releaseFailure)
            {
                _failure = new AggregateException(
                    "fast path が fault 停止し、所有 output の release も失敗しました（出力経路側の独立した解放機構が release を引き継ぎます）。",
                    ex,
                    releaseFailure);
            }
        }
    }

    /// <summary>trace が off なら何もしない（enqueue 自体を行わない構成で既存 test 挙動に影響を出さない）。</summary>
    private void RecordTrace(
        PhysicalInput input,
        string layerId,
        IReadOnlyList<MappedOutputEdge> edges,
        bool emitted,
        double dispatchCompletedMonotonicMs)
    {
        if (!_traceEnabled)
        {
            return;
        }

        var entry = new InputTraceEntry(
            input.DeviceInstanceId,
            input.ControlId,
            input.Edge,
            layerId,
            edges.Select(edge => edge.Output).ToArray(),
            emitted,
            input.MonotonicMs,
            dispatchCompletedMonotonicMs,
            Math.Max(0, dispatchCompletedMonotonicMs - input.MonotonicMs),
            Interlocked.Increment(ref _traceSequence));

        _traceBuffer.Enqueue(entry);
        if (Interlocked.Increment(ref _traceApproxCount) > _traceCapacity && _traceBuffer.TryDequeue(out _))
        {
            Interlocked.Decrement(ref _traceApproxCount);
        }
    }

    /// <summary>buffer に溜まっている trace を全て取り出す（main thread から呼ぶ。worker を待たない）。</summary>
    public IReadOnlyList<InputTraceEntry> DrainTrace()
    {
        if (_traceBuffer.IsEmpty)
        {
            return [];
        }

        var drained = new List<InputTraceEntry>();
        while (_traceBuffer.TryDequeue(out var entry))
        {
            Interlocked.Decrement(ref _traceApproxCount);
            drained.Add(entry);
        }

        return drained;
    }

    private void ReleaseAllOwnedOutputs()
    {
        foreach (var runtime in _runtimes.Values)
        {
            _emitter.Emit(runtime.StopAndReleaseAll());
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_started && _failure is null && _worker.IsAlive)
        {
            Stop();
        }
        _disposed = true;
        _controlWake.Dispose();
    }

    private static double MonotonicMilliseconds() =>
        Stopwatch.GetTimestamp() * 1000d / Stopwatch.Frequency;
}
