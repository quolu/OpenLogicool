# Playbook schema update と rollback 契約

Playbook、Execution Journal、Knowledge Pack の schema update は `SchemaRollback.Plan` で三つの境界ごとに明示する。

- `0.1.0` は現在確認済みの schema version である。未知 version は update 計画の作成時と rollback 時の両方で失敗し、読み飛ばさない。
- rollback は update の逆順・逆方向の `SchemaChange` を返す口だけを持つ。データを黙って変換・保存しない。
- Playbook materializer、Execution Journal、Knowledge Pack validator、および各 store の既存の検証・永続化・fold は再実装しない。rollback を適用する呼び出し側は、それら既存境界を通す。
- 現在は一つの確認済み version だけを登録する。将来の schema version を追加する時は、その version、各境界の forward update、逆向き rollback を同じ release で明示しなければならない。
