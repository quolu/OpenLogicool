# t06-test-field-diagnostics 完了証跡

- 実装: implementer（sonnet×medium）委譲・統括が diff 実読（FastPathPump の trace 経路）と focused test 再実行・データ有り diagnostics 実走で受入
- test field: FastPathPump に有界 trace buffer（既定 256・drop-oldest・ConcurrentQueue＋Interlocked のみ＝非 blocking）。trace off（既定）は enqueue 自体なし。`run --trace` で 1 行 1 event 表示（device・control・edge・解決 layer・解決 token・割当有無）
- diagnostics CLI: db・devices（実機 instance id）・profiles・app associations・workspaces（revision 数）・foreground identity（path/package）・watchdog 所在を read-only 表示
- 検証: Input 79件（+4）・Host 10件・Architecture 4件 green（worker＋統括の両方）。diagnostics はデータ 0 件（worker・実機2台検出）とデータ有り（統括・profile 2/workspace revision 5 件）の両方で exit 0
- 未実測: 実機押下での `run --trace` 表示はオーナー手番（t03 の実機実測と同時に行う）
