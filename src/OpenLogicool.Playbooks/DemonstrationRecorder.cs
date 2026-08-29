using OpenLogicool.Contracts.Exploration;
using OpenLogicool.Contracts.Perception;
using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Contracts.Shared;

namespace OpenLogicool.Playbooks;

/// <summary>記録器が受理しなかった入力の内訳。捨てた事実を隠さず数える。</summary>
public sealed record DemonstrationRecorderCounters(
    long IgnoredWhilePaused,
    long IgnoredOutsideClientFrame,
    long UnpairedReleases,
    long DiscardedHeldPresses);

/// <summary>
/// 利用者の実操作を操作デモ原本へ書く記録器。
///
/// 入力の取り方（OS hook・device adapter）は所有しない——<see cref="DemonstrationInputEdge"/>を
/// 受け取るだけで、mouse／keyboardのOS取得は環境別adapterが持つ。
/// 観測面は<see cref="IDemonstrationObservationRuntime"/>で、dispatchのmethodを持たないため
/// 記録器は構造上、入力を出せない。
///
/// 束縛の順序: 記録開始後にcurrent observationを持ち、押下時点でその観測とframeへ束縛する。
/// 押下直前だけ観測してbeforeを作ることはしない。
/// </summary>
public sealed class DemonstrationRecorder
{
    private readonly IDemonstrationSessionStore store;
    private readonly IDemonstrationObservationRuntime runtime;
    private readonly Func<DemonstrationScreenPoint, IReadOnlyList<double>?> normalize;
    private readonly DemonstrationRecordingGate gate;
    private readonly ExplorationWaitCondition waitCondition;
    private readonly Dictionary<string, PendingPress> pending = new(StringComparer.Ordinal);

    private DemonstrationSessionDraft? session;
    private ObservedScene? current;
    private long ignoredWhilePaused;
    private long ignoredOutsideClientFrame;
    private long unpairedReleases;
    private long discardedHeldPresses;
    private long operationSequence;

    public DemonstrationRecorder(
        IDemonstrationSessionStore store,
        IDemonstrationObservationRuntime runtime,
        Func<DemonstrationScreenPoint, IReadOnlyList<double>?> normalize,
        DemonstrationRecordingGate gate,
        ExplorationWaitCondition waitCondition)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(normalize);
        ArgumentNullException.ThrowIfNull(gate);
        ArgumentNullException.ThrowIfNull(waitCondition);

        this.store = store;
        this.runtime = runtime;
        this.normalize = normalize;
        this.gate = gate;
        this.waitCondition = waitCondition;
    }

    public DemonstrationRecorderStatus Status { get; private set; } = DemonstrationRecorderStatus.Idle;

    public string? SessionId => session?.SessionId;

    /// <summary>押下中で、まだ離されていない入力の数。</summary>
    public int HeldPressCount => pending.Count;

    public DemonstrationRecorderCounters Counters => new(
        ignoredWhilePaused,
        ignoredOutsideClientFrame,
        unpairedReleases,
        discardedHeldPresses);

    /// <summary>
    /// 記録を開始する。再生中は排他に阻まれて開始しない。
    /// 開始時に一度観測し、以後の押下はその時点のcurrent observationへ束縛される。
    /// </summary>
    public async Task<DemonstrationSessionRecord> StartAsync(
        DemonstrationSessionDraft draft,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (Status is DemonstrationRecorderStatus.Recording or DemonstrationRecorderStatus.Paused)
        {
            throw new InvalidOperationException("既に記録中です。");
        }

        if (!gate.TryBeginRecording(out var refusal))
        {
            throw new InvalidOperationException(refusal);
        }

        try
        {
            var record = store.Start(draft);
            session = draft;
            current = await ObserveSceneAsync(cancellationToken).ConfigureAwait(false);
            pending.Clear();
            operationSequence = 0;
            Status = DemonstrationRecorderStatus.Recording;
            return record;
        }
        catch
        {
            gate.EndRecording();
            session = null;
            throw;
        }
    }

    /// <summary>current observationを取り直す。押下はこの最新観測へ束縛される。</summary>
    public async Task RefreshObservationAsync(CancellationToken cancellationToken = default)
    {
        if (Status != DemonstrationRecorderStatus.Recording)
        {
            return;
        }

        current = await ObserveSceneAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// foregroundの現在値を伝える。対象appから外れた区間はpause eventとして原本へ残し、
    /// その間の入力は受け取らない。復帰は新しいObservationから再開する。
    /// pathがnullなのはforeground identityを取得できなかった場合で、そのままnullで残す。
    /// </summary>
    public async Task ObserveForegroundAsync(
        string? foregroundApplicationPath,
        DateTimeOffset occurredUtc,
        CancellationToken cancellationToken = default)
    {
        var active = RequireSession();
        var isTarget = foregroundApplicationPath is not null
            && string.Equals(foregroundApplicationPath, active.TargetApplicationPath, StringComparison.OrdinalIgnoreCase);

        if (Status == DemonstrationRecorderStatus.Recording && !isTarget)
        {
            discardedHeldPresses += pending.Count;
            pending.Clear();
            current = null;
            Append(new DemonstrationEventDraft(
                ContractSchemaVersions.Revision03,
                active.SessionId,
                DemonstrationEventKind.FocusLost,
                occurredUtc,
                FocusChange: new DemonstrationFocusChange(
                    ContractSchemaVersions.Revision03, foregroundApplicationPath, null, occurredUtc)));
            Status = DemonstrationRecorderStatus.Paused;
            return;
        }

        if (Status == DemonstrationRecorderStatus.Paused && isTarget)
        {
            var resumed = await ObserveSceneAsync(cancellationToken).ConfigureAwait(false);
            Append(new DemonstrationEventDraft(
                ContractSchemaVersions.Revision03,
                active.SessionId,
                DemonstrationEventKind.FocusRegained,
                occurredUtc,
                FocusChange: new DemonstrationFocusChange(
                    ContractSchemaVersions.Revision03,
                    active.TargetApplicationPath,
                    resumed.ObservationId,
                    occurredUtc)));
            current = resumed;
            Status = DemonstrationRecorderStatus.Recording;
        }
    }

    /// <summary>
    /// 生入力edgeを1件処理する。押下は保留し、対になる解放が来た有限操作だけを原本へ書く。
    /// 対にならなかった押下（記録停止・focus喪失を跨いだ押しっぱなし）は操作にしない。
    /// </summary>
    public async Task HandleAsync(
        DemonstrationInputEdge edge,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(edge);
        var active = RequireSession();

        if (Status != DemonstrationRecorderStatus.Recording)
        {
            ignoredWhilePaused++;
            return;
        }

        switch (edge.Kind)
        {
            case DemonstrationInputEdgeKind.PointerDown:
            case DemonstrationInputEdgeKind.KeyDown:
                BeginPress(edge);
                return;

            case DemonstrationInputEdgeKind.PointerUp:
            case DemonstrationInputEdgeKind.KeyUp:
                await CompletePressAsync(active, edge, cancellationToken).ConfigureAwait(false);
                return;

            case DemonstrationInputEdgeKind.Wheel:
                await AppendWheelAsync(active, edge, cancellationToken).ConfigureAwait(false);
                return;

            default:
                throw new ArgumentException($"未知のDemonstrationInputEdgeKind '{edge.Kind}' です。", nameof(edge));
        }
    }

    /// <summary>記録を停止する。押しっぱなしの入力は操作にせず、停止を原本へ書いて排他を返す。</summary>
    public DemonstrationSessionRecord StopAsync(string reason, DateTimeOffset occurredUtc)
    {
        var active = RequireSession();
        discardedHeldPresses += pending.Count;
        pending.Clear();
        Append(new DemonstrationEventDraft(
            ContractSchemaVersions.Revision03,
            active.SessionId,
            DemonstrationEventKind.Stopped,
            occurredUtc,
            Stop: new DemonstrationStop(ContractSchemaVersions.Revision03, reason, occurredUtc)));
        Status = DemonstrationRecorderStatus.Stopped;
        current = null;
        gate.EndRecording();
        return store.Load(active.SessionId)
            ?? throw new InvalidOperationException($"停止した原本 '{active.SessionId}' を読み出せません。");
    }

    private void BeginPress(DemonstrationInputEdge edge)
    {
        var scene = current
            ?? throw new InvalidOperationException("記録中のcurrent observationがありません。");

        IReadOnlyList<double>? normalizedPoint = null;
        if (edge.Kind == DemonstrationInputEdgeKind.PointerDown)
        {
            var screenPoint = edge.ScreenPoint
                ?? throw new ArgumentException("pointer押下にはScreenPointが必要です。", nameof(edge));
            normalizedPoint = normalize(screenPoint);
            if (normalizedPoint is null)
            {
                ignoredOutsideClientFrame++;
                return;
            }
        }

        pending[edge.ControlId] = new PendingPress(edge, scene, normalizedPoint);
    }

    private async Task CompletePressAsync(
        DemonstrationSessionDraft active,
        DemonstrationInputEdge edge,
        CancellationToken cancellationToken)
    {
        if (!pending.Remove(edge.ControlId, out var press))
        {
            unpairedReleases++;
            return;
        }

        var isPointer = press.Edge.Kind == DemonstrationInputEdgeKind.PointerDown;
        string operation;
        IReadOnlyList<double>? destination = null;

        if (isPointer)
        {
            var releasePoint = edge.ScreenPoint
                ?? throw new ArgumentException("pointer解放にはScreenPointが必要です。", nameof(edge));
            if (releasePoint == press.Edge.ScreenPoint)
            {
                operation = GameInteractionOperations.Click;
            }
            else
            {
                destination = normalize(releasePoint);
                if (destination is null)
                {
                    // client frameの外で離した引きずりは、この原本の操作として書けない。
                    ignoredOutsideClientFrame++;
                    return;
                }

                operation = GameInteractionOperations.Drag;
            }
        }
        else
        {
            operation = GameInteractionOperations.KeyTap;
        }

        await AppendOperationAsync(
            active,
            press.Scene,
            new DemonstrationFrameBinding(
                ContractSchemaVersions.Revision03,
                press.Scene.ObservationId,
                press.Scene.Frame.Sequence,
                press.Scene.Frame.TransformRevision,
                active.TargetWindowSourceId,
                press.NormalizedPoint),
            operation,
            press.Edge.Source,
            press.Edge,
            edge,
            keyTokens: isPointer ? null : [press.Edge.OutputToken],
            deviceControlId: DeviceControlIdOf(press.Edge),
            verticalScrollSteps: null,
            horizontalScrollSteps: null,
            dragDestination: destination,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task AppendWheelAsync(
        DemonstrationSessionDraft active,
        DemonstrationInputEdge edge,
        CancellationToken cancellationToken)
    {
        var scene = current
            ?? throw new InvalidOperationException("記録中のcurrent observationがありません。");
        if (edge.WheelVerticalSteps == 0 && edge.WheelHorizontalSteps == 0)
        {
            throw new ArgumentException("wheel edgeの段数が0です。", nameof(edge));
        }

        var screenPoint = edge.ScreenPoint
            ?? throw new ArgumentException("wheel edgeにはScreenPointが必要です。", nameof(edge));
        var normalizedPoint = normalize(screenPoint);
        if (normalizedPoint is null)
        {
            ignoredOutsideClientFrame++;
            return;
        }

        await AppendOperationAsync(
            active,
            scene,
            new DemonstrationFrameBinding(
                ContractSchemaVersions.Revision03,
                scene.ObservationId,
                scene.Frame.Sequence,
                scene.Frame.TransformRevision,
                active.TargetWindowSourceId,
                normalizedPoint),
            GameInteractionOperations.Scroll,
            edge.Source,
            edge,
            edge,
            keyTokens: null,
            deviceControlId: DeviceControlIdOf(edge),
            verticalScrollSteps: edge.WheelVerticalSteps == 0 ? null : edge.WheelVerticalSteps,
            horizontalScrollSteps: edge.WheelHorizontalSteps == 0 ? null : edge.WheelHorizontalSteps,
            dragDestination: null,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task AppendOperationAsync(
        DemonstrationSessionDraft active,
        ObservedScene before,
        DemonstrationFrameBinding binding,
        string operation,
        DemonstrationInputSource source,
        DemonstrationInputEdge pressEdge,
        DemonstrationInputEdge releaseEdge,
        IReadOnlyList<string>? keyTokens,
        string? deviceControlId,
        int? verticalScrollSteps,
        int? horizontalScrollSteps,
        IReadOnlyList<double>? dragDestination,
        CancellationToken cancellationToken)
    {
        var stability = await runtime.WaitStableAsync(before, waitCondition, cancellationToken).ConfigureAwait(false);
        var comparison = runtime.Compare(before, stability);
        operationSequence++;

        var demonstrationOperation = new DemonstrationOperation(
            ContractSchemaVersions.Revision03,
            $"{active.SessionId}:op-{operationSequence}",
            operation,
            source,
            binding,
            before,
            stability,
            comparison,
            $"{active.SessionId}:evidence-{operationSequence}",
            (long)Math.Round(pressEdge.MonotonicMs),
            (long)Math.Round(releaseEdge.MonotonicMs) + stability.ElapsedMilliseconds,
            pressEdge.OccurredUtc,
            keyTokens,
            deviceControlId,
            verticalScrollSteps,
            horizontalScrollSteps,
            dragDestination);

        Append(new DemonstrationEventDraft(
            ContractSchemaVersions.Revision03,
            active.SessionId,
            DemonstrationEventKind.Operation,
            pressEdge.OccurredUtc,
            Operation: demonstrationOperation));

        // 操作後の安定画面が次の押下のbeforeになる。安定しなかった場合は次のRefreshで取り直す。
        current = stability.StableScene ?? current;
    }

    private async Task<ObservedScene> ObserveSceneAsync(CancellationToken cancellationToken)
    {
        var observation = await runtime.ObserveAsync(cancellationToken).ConfigureAwait(false);
        return await runtime.DiscoverTargetsAsync(observation, cancellationToken).ConfigureAwait(false);
    }

    private void Append(DemonstrationEventDraft draft) => store.Append(draft);

    private DemonstrationSessionDraft RequireSession() =>
        session ?? throw new InvalidOperationException("記録が開始されていません。");

    private static string? DeviceControlIdOf(DemonstrationInputEdge edge) =>
        edge.Source is DemonstrationInputSource.G13 or DemonstrationInputSource.G600
            ? edge.ControlId
            : null;

    private sealed record PendingPress(
        DemonstrationInputEdge Edge,
        ObservedScene Scene,
        IReadOnlyList<double>? NormalizedPoint);
}
