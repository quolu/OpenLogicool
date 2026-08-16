# t01-close-revision-core 完了証跡

- 実装 commit: 33c2dae（origin/main へ push 済み）
- focused test: Profiles 18件・Persistence 15件・Host 9件 green（2026-08-16）
- CLI 実走（temp DB）: dry-run→適用（revision 1）→変更版適用（revision 3）→undo 無指定（rev2→rev4）→undo 番号指定（rev1→rev5）→export→export 品の再取込（revision 2）の全往復成立。段階別報告（編集/保存/runtime/device）が各段階で表示され、保存成功は「未適用/未反映」と区別された
- APP-007: 保存は単一 SQLite transaction（部分保存の構造的排除）・runtime 適用状態は常駐 mutex 観測で区別・device 反映は「対象外」明示
- MAP-009: append-only revision store（migration 004）・undo・export・import（workspace command と同一フォーマット）
