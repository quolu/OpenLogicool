# t02 LGS XML import dry-run

## 実装

- `LgsXmlDryRun.Analyze` を追加し、LGS 9.04.49 profile XML から変換候補と未対応行を分離した。
- 利用者変更の単一 keystroke 割当だけを候補にし、`original="true"`、script macro、`target@path` は未対応として表示する。
- parser は DTD を拒否し、script・path の値を実行またはパス解決しない。保存・device API・LGS 操作も行わない pure dry-run である。

## 検証

`dotnet test tests/OpenLogicool.Profiles.Tests/OpenLogicool.Profiles.Tests.csproj --no-restore --filter FullyQualifiedName~LgsXmlDryRunTests`

- 2 tests passed / exit 0
- fixture XML で候補1行と未対応3行（path、script、既定割当）を確認した。
- 外部 entity を含む XML が DTD 拒否で失敗することを確認した。
