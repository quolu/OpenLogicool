# t05 restart ownership reconcile — evidence

- task: `phase8b-game-operator-dist/t05-restart-ownership-reconcile`
- scope: host restart 後の output ownership gate、focused Playbooks test

## 判定

- host 再起動後の gate は `PendingReconciliation` で開始し、次 dispatch を禁止する。
- watchdog の release 完了が確認できない限り、gate は解錠しない。
- release 確認後だけ `Reconciled` となり、既存 `AttemptDispatchGate` を呼べる。
- watchdog の release protocol と `AttemptDispatchGate` は変更・再実装していない。

## focused verification

`dotnet test tests/OpenLogicool.Playbooks.Tests/OpenLogicool.Playbooks.Tests.csproj --filter FullyQualifiedName~RestartOwnershipTests`
