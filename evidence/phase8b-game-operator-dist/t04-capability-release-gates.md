# t04-capability-release-gates 証跡

## 実装

- Observe Only、Teach、Supervised、Verified ごとの release 設定と公開可否 decision を追加した。
- 既存 `GamePolicyGate` を規約判定へ、既存 `VerifiedEnvScope` を Verified の環境一致判定へ使う。
- mode 実装、proposal 処理、規約解釈、環境スコープの実装は変更していない。

## focused test

`dotnet test tests/OpenLogicool.Playbooks.Tests/OpenLogicool.Playbooks.Tests.csproj --filter FullyQualifiedName~CapabilityReleaseTests`

- 結果: 合格 3、失敗 0。
- 各 capability が自分の release 設定を必要とすることを確認する。
- Observe／Supervised が対応する規約 mode 許可を迂回しないことを確認する。
- Verified が Auto 許可だけでなく、完全一致する Verified 環境も必要とすることを確認する。
