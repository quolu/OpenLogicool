# t03 — proposal reject

## 実施

- Playbooks に pure な `ProposalReject` gate を追加した。gate は proposal を dispatch 前に
  schema、action catalog、現在 state、期待 risk の順で照合し、理由付き decision だけを返す。
- schema 外、catalog 外、state 不一致、risk 不一致をそれぞれ拒否する。
- gate は InputEmitter、device API、永続化 API、dispatch delegate を参照しない。catalog と risk を
  照合できない Teach action も拒否し、Supervised 承認口ができるまで外部入力へ到達させない。

## 検証

`dotnet test tests/OpenLogicool.Playbooks.Tests/OpenLogicool.Playbooks.Tests.csproj --no-restore --artifacts-path C:\\Users\\kite_\\AppData\\Local\\Temp\\openlogicool-phase6-t03-artifacts-54228 --filter "FullyQualifiedName~ProposalRejectTests" --logger "console;verbosity=normal"`

- Windows native focused test: exit 0（5 passed / 0 failed）
- 受理1件、schema／catalog／state／risk の拒否4件を検査

## 変更ファイル

- `src/OpenLogicool.Playbooks/ProposalReject.cs`
- `tests/OpenLogicool.Playbooks.Tests/ProposalRejectTests.cs`
- `docs/contracts/proposal-reject.md`
- `evidence/phase6-ai-teach/t03-proposal-reject.md`
