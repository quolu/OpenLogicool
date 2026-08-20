# t05 — Observe Only

## 実施

- Playbooks に `ObserveOnly` を追加した。`INextActionPlanner` へ context を渡し、返った
  `NextActionProposal` を観測結果として返すだけの API である。
- API は Attempt、RunJournal、dispatch delegate、InputEmitter、PlaybookVersion を参照しない。
  proposal が出ても外部入力を実行せず、Playbook を書き換えない。

## 検証

`dotnet test tests/OpenLogicool.Playbooks.Tests/OpenLogicool.Playbooks.Tests.csproj --no-restore --artifacts-path C:\\Users\\kite_\\AppData\\Local\\Temp\\openlogicool-phase6-t05-artifacts-54228 --filter "FullyQualifiedName~ObserveOnlyTests" --logger "console;verbosity=normal"`

- Windows native focused test: exit 0（2 passed / 0 failed）
- planner の proposal 取得だけを実行し、Attempt／journal／dispatch／PlaybookVersion の依存がないことを検査

## 変更ファイル

- `src/OpenLogicool.Playbooks/ObserveOnly.cs`
- `tests/OpenLogicool.Playbooks.Tests/ObserveOnlyTests.cs`
- `docs/contracts/observe-only.md`
- `evidence/phase6-ai-teach/t05-observe-only.md`
