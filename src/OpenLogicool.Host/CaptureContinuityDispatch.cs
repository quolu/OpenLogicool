using OpenLogicool.Capture;
using OpenLogicool.Contracts.Capture;
using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Playbooks;

namespace OpenLogicool.Host;

/// <summary>
/// Capture の連続性を確認してから、Playbooks の一手 dispatch を通す製品統合経路。
/// Capture が不連続なら外部入力 delegate へ到達しない。
/// </summary>
public sealed class CaptureContinuityDispatch(RunControls controls, CaptureContinuityGate continuityGate)
{
    private readonly RunControls controls = controls ?? throw new ArgumentNullException(nameof(controls));
    private readonly CaptureContinuityGate continuityGate = continuityGate ?? throw new ArgumentNullException(nameof(continuityGate));

    /// <summary>
    /// 連続性がある時だけ、既存の DispatchArmed→外部入力の順序を実行する。
    /// false は capture 不連続による未送信であり、Attempt を arm しない。
    /// </summary>
    public bool TryStepOnce(RunEvent dispatchEvent, Action externalInput)
    {
        ArgumentNullException.ThrowIfNull(dispatchEvent);
        ArgumentNullException.ThrowIfNull(externalInput);

        if (!continuityGate.AllowsAutomaticInput)
        {
            return false;
        }

        controls.StepOnce(dispatchEvent, externalInput);
        return true;
    }

    /// <summary>実画面再開の照合が UniqueMatch と対象一致を満たす時だけ dispatch する。</summary>
    public bool TryResumeStepOnce(
        LiveResumeBinding binding,
        LiveResumeContext context,
        IReadOnlyList<RunEvent> events,
        string expectedStateId,
        long freshnessBudgetMs,
        long stabilityWindowMs,
        RunEvent dispatchEvent,
        Action externalInput)
    {
        var decision = LiveResumeGate.Judge(
            binding,
            context,
            events,
            expectedStateId,
            freshnessBudgetMs,
            stabilityWindowMs);
        return decision.DispatchAllowed && TryStepOnce(dispatchEvent, externalInput);
    }
}

/// <summary>
/// Host が capture read と dispatch 境界を接続する一回分の loop。
/// capture の取得・再校正・dispatch は fast path と別の呼び出し側が明示的に順序付ける。
/// </summary>
public sealed class CaptureContinuityDispatchLoop(CaptureContinuityDispatch dispatch, CaptureContinuityGate continuityGate)
{
    private readonly CaptureContinuityDispatch dispatch = dispatch ?? throw new ArgumentNullException(nameof(dispatch));
    private readonly CaptureContinuityGate continuityGate = continuityGate ?? throw new ArgumentNullException(nameof(continuityGate));

    /// <summary>
    /// 現在の capture read を gate へ反映した後、明示的に渡された同一 frame だけで再校正し、
    /// 通常の一手を dispatch する。再校正の省略は既存の block 状態を維持する。
    /// </summary>
    public bool TryStepOnce(
        CaptureRead read,
        long staleAfterMs,
        CapturedFrame? recalibrationFrame,
        RunEvent dispatchEvent,
        Action externalInput)
    {
        ObserveAndMaybeRecalibrate(read, staleAfterMs, recalibrationFrame);
        return dispatch.TryStepOnce(dispatchEvent, externalInput);
    }

    /// <summary>
    /// 上と同じ capture continuity を通した上で、resume の UniqueMatch 条件も満たす時だけ dispatch する。
    /// </summary>
    public bool TryResumeStepOnce(
        CaptureRead read,
        long staleAfterMs,
        CapturedFrame? recalibrationFrame,
        LiveResumeBinding binding,
        LiveResumeContext context,
        IReadOnlyList<RunEvent> events,
        string expectedStateId,
        long freshnessBudgetMs,
        long stabilityWindowMs,
        RunEvent dispatchEvent,
        Action externalInput)
    {
        ObserveAndMaybeRecalibrate(read, staleAfterMs, recalibrationFrame);
        return dispatch.TryResumeStepOnce(
            binding,
            context,
            events,
            expectedStateId,
            freshnessBudgetMs,
            stabilityWindowMs,
            dispatchEvent,
            externalInput);
    }

    private void ObserveAndMaybeRecalibrate(CaptureRead read, long staleAfterMs, CapturedFrame? recalibrationFrame)
    {
        ArgumentNullException.ThrowIfNull(read);
        continuityGate.Observe(read, staleAfterMs);
        if (recalibrationFrame is not null)
        {
            continuityGate.Recalibrate(recalibrationFrame);
        }
    }
}
