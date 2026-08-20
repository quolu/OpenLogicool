using OpenLogicool.Capture;
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
}
