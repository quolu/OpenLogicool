using OpenLogicool.Contracts.Devices.Shared;

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
    private readonly Thread _worker;
    private volatile bool _stopRequested;
    private volatile bool _started;
    private Exception? _failure;
    private long _processedCount;

    public FastPathPump(
        IReadOnlyList<FastPathSource> sources,
        IReadOnlyDictionary<string, DeviceMappingRuntime> runtimesByDeviceInstanceId,
        IOutputEmitter emitter)
    {
        _sources = sources;
        _runtimes = runtimesByDeviceInstanceId;
        _emitter = emitter;
        _worker = new Thread(Worker) { IsBackground = true, Name = "OpenLogicoolFastPath" };
    }

    /// <summary>worker が fault 停止した原因（null なら正常）。</summary>
    public Exception? Failure => _failure;

    public bool IsRunning => _started && !_stopRequested && _failure is null && _worker.IsAlive;

    /// <summary>処理済み PhysicalInput 件数。</summary>
    public long ProcessedCount => Interlocked.Read(ref _processedCount);

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
    public int RunOnce()
    {
        var processed = 0;
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

                _emitter.Emit(runtime.Process(input));
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
                    Thread.Sleep(1);
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
                    "fast path が fault 停止し、所有 output の release も失敗しました（watchdog が process 終了時に release します）。",
                    ex,
                    releaseFailure);
            }
        }
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
        if (_started && _failure is null && _worker.IsAlive)
        {
            Stop();
        }
    }
}
