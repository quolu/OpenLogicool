# t01 Planner proposal schema

## 実施

- `PlannerContext` と `NextActionProposal` の全必須 field を `PlannerProposalSchema.Validate` で検証した。
- 最上位と action、precondition、expected outcome、stability window、stop、validity、budget の schema version は
  `0.1.0` だけを受理し、未知版を明示的に拒否する。
- proposal mode と action 種別の対応、budget、期限、stability、validity の境界も検証する。

## 検証

```text
dotnet test tests/OpenLogicool.Conformance.Tests/OpenLogicool.Conformance.Tests.csproj --nologo --filter FullyQualifiedName~PlannerProposalSchemaTests --logger console;verbosity=minimal

成功: 合格 3、失敗 0、スキップ 0
```

focused conformance は正常な Teach proposal の受理、ネストした未知 schema version の拒否、mode と action の不一致拒否を確認した。
