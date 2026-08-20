# t02 — AI isolation

## 実施

- `OpenLogicool.AI` の project reference を、AI-002 の境界である
  `OpenLogicool.Contracts` のみに固定する architecture test を追加した。
- AI が直接参照できない対象として `OpenLogicool.Input`、G13/G600 device、
  `OpenLogicool.Persistence`、`OpenLogicool.Capture` を明示的に検査する。
- AI の公開口は Contracts の `INextActionPlanner`／`NextActionProposal` に留まり、
  input・DB・device・capture の実装プロジェクトへ project dependency を持たない。

## 検証

`dotnet test tests/OpenLogicool.Architecture.Tests/OpenLogicool.Architecture.Tests.csproj --no-restore --artifacts-path C:\\Users\\kite_\\AppData\\Local\\Temp\\openlogicool-phase6-t02-artifacts-54228 --logger "console;verbosity=normal"`

- Windows native focused test: exit 0（5 passed / 0 failed）

## 変更ファイル

- `tests/OpenLogicool.Architecture.Tests/ProjectReferenceDirectionTests.cs`
- `evidence/phase6-ai-teach/t02-ai-isolation.md`
