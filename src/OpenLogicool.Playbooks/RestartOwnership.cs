namespace OpenLogicool.Playbooks;

public enum RestartOwnershipState
{
    PendingReconciliation,
    Reconciled,
}

/// <summary>
/// host 再起動直後の output ownership を表す小さな gate。
/// watchdog による release と AttemptDispatchGate の dispatch 自体は既存の責務として呼ばない。
/// </summary>
public sealed class RestartOwnership
{
    private RestartOwnershipState _state = RestartOwnershipState.PendingReconciliation;

    private RestartOwnership()
    {
    }

    public RestartOwnershipState State => _state;

    public bool CanDispatch => _state == RestartOwnershipState.Reconciled;

    /// <summary>host 再起動時は、前 host が所有した output の状態が不明として開始する。</summary>
    public static RestartOwnership AfterHostRestart() => new();

    /// <summary>
    /// watchdog の死亡時 release が完了したという外部観測を受けてからだけ dispatch を解錠する。
    /// release 未確認のまま解錠する経路は持たない。
    /// </summary>
    public void CompleteReconciliation(bool priorOutputReleaseConfirmed)
    {
        if (!priorOutputReleaseConfirmed)
        {
            throw new InvalidOperationException("前 host の output release が確認されるまで dispatch を解錠できません。");
        }

        _state = RestartOwnershipState.Reconciled;
    }

    /// <summary>既存 AttemptDispatchGate を呼ぶ直前に、reconcile 未完了なら明示停止する。</summary>
    public void RequireDispatchAllowed()
    {
        if (!CanDispatch)
        {
            throw new InvalidOperationException("host 再起動後の output ownership reconciliation が未完了です。次の dispatch は禁止されます。");
        }
    }
}
