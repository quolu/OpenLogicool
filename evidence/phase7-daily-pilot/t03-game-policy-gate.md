# t03 Game policy gate

## 実施

- `GamePolicyRecord` に確認状態と Observe／Assist／Auto の mode 別許可を記録した。
- Unverified／Changed／InterpretationUnknown は Assist と Auto を強制 disable する。
- gate は SendInput 結果、import 元、dispatch delegate を受け取らず、技術的な入力可否で policy を迂回できない。

## 検証

```text
dotnet test tests/OpenLogicool.Playbooks.Tests/OpenLogicool.Playbooks.Tests.csproj --nologo --filter FullyQualifiedName~GamePolicyGateTests --logger console;verbosity=minimal

成功: 合格 4、失敗 0、スキップ 0
```

focused test で、未確認三状態の Assist／Auto 拒否と、確認済みでも mode 許可がない imported record の
Auto 拒否を確認した。
