# t04 EXP-AI-01 harness

## 実施

- Phase 5 frozen corpus item を `PlannerContext` と期待 action key で評価する provider 非依存 harness を追加した。
- 正確さ、unknown 棄却、latency、cost、cancel を `FrozenEvaluationReport` に集計する。
- harness は provider client、credential、prompt、dispatch を持たず、acceptance を prompt 調整へ渡す API を置かない。

## 検証

```text
dotnet test tests/OpenLogicool.AI.Tests/OpenLogicool.AI.Tests.csproj --nologo --logger console;verbosity=minimal

成功: 合格 2、失敗 0、スキップ 0
```

focused test で、既知／unknown の集計と cancel 済み evaluation の非実行を確認した。
