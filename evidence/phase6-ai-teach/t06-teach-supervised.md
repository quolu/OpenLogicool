# t06 Teach／Supervised port

## 実施

- fake を含む `INextActionPlanner` から Teach proposal 一件を取得し、承認待ちの `PendingTeachStep` として保持する口を追加した。
- 利用者の明示 `approvalId` がある場合だけ `ApprovedTeachStep` へ移す。
- module は provider client、dispatch delegate、InputEmitter、device API、SendInput を持たない。

## 検証

```text
dotnet test tests/OpenLogicool.Playbooks.Tests/OpenLogicool.Playbooks.Tests.csproj --nologo --filter FullyQualifiedName~TeachSupervisedTests --logger console;verbosity=minimal

成功: 合格 2、失敗 0、スキップ 0
```

focused test で、fake planner からの Teach proposal が承認待ちを経て承認済みの一手になることと、
Teach 以外を拒否することを確認した。
