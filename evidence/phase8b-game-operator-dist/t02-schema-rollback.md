# t02-schema-rollback 証跡

## 実装

- `SchemaRollback` に Playbook、Execution Journal、Knowledge Pack の update 計画と、逆順・逆方向の rollback 口を追加した。
- 現在確認済みの `0.1.0` だけを受理し、未知 version は update 計画と rollback の双方で例外にする。
- 既存の store、Playbook materializer、RunJournal、KnowledgePackValidator の実装は変更・再実装していない。

## focused test

`dotnet test tests/OpenLogicool.Playbooks.Tests/OpenLogicool.Playbooks.Tests.csproj --filter FullyQualifiedName~SchemaRollbackTests`

- 結果: 合格 2、失敗 0。
- 既知 schema の3境界で rollback が逆順・逆方向になることを確認した。
- 未知 version が update と rollback の両方で拒否されることを確認した。
