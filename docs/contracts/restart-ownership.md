# Restart ownership reconciliation

`RestartOwnership` は host 再起動後、次の dispatch を許可するための pure gate である。

1. `AfterHostRestart` は必ず `PendingReconciliation` で開始する。この間の `RequireDispatchAllowed` は明示エラーにする。
2. watchdog の死亡時 release が完了したという外部観測を `CompleteReconciliation(true)` へ渡したときだけ `Reconciled` へ進む。
3. release 未確認の `CompleteReconciliation(false)` はエラーであり、gate を解錠しない。
4. `Reconciled` 後だけ既存 `AttemptDispatchGate` の dispatch を呼べる。

この型は watchdog の process／release protocol も、Attempt／journal／dispatch の状態機械も再実装しない。前者の観測結果と後者の呼出し前境界を接続するだけである。
