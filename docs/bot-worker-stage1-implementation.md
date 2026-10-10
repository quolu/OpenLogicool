# Bot process 分離・段階1の実装と未確認事項

設計の正本は [bot-worker-process-design.md](bot-worker-process-design.md)。この文書は段階1の実装結果と、導入後に確かめる項目を残す。

実装は `bot-worker-stage1` の worktree 内だけで行った。本体の操作口・UI の配線・遠隔表示は変更していない。段階2・3は未実装で、機能とモードの一覧は本体起動時の設定に固定されたまま。

## 実装

- 同じ Host の `bot-worker` 入口で `VisualKeyAssistRuntime.RunAsync` を動かす。子は Nano 接続・単一起動 Mutex・本体の操作用パイプを作らない。
- protocol 1、実行ごとの乱数名、CurrentUserOnly、UTF-8 の行区切り JSON、入力と問い合わせの2本のパイプを使う。6操作・物理ボタン・モード・片道イベントを渡す。`pointer-unmoved` は元の例外へ戻し、応答消失後に入力を再送しない。
- 本体の受付確定だけを短い lock で囲む。装置の呼出し・JSON・終了待ちは外へ置く。停止・切断・子の終了で受付を閉じ、受付済みの有限入力が終わってから番を空ける。
- 借用した常駐 Nano には終了処理を行わない。本体が専有接続を開いた場合も、受付済み入力の回収後は `ALL_UP` を送らずに接続を閉じる。通常の常駐終了は既存の一括解放を維持する。
- 標準入力の EOF を起動直後から独立して監視する。Windows の同期標準入力の読取を通常終了で待たず、監視用 reader と cancellation source の寿命を子 process に揃える。
- Job Object への登録が済んでから、本体が標準入力へ `start` を送る。登録前の本体死亡は EOF で停止し、登録後は Job Object の終了連動も使う。
- 停止期限は5秒。超過時は `worker-stop-timeout.json` に理由を記録して子を終了する。本体の受付済み入力は、その後も完了を待つ。標準出力・標準エラーは `worker-stdout.log` / `worker-stderr.log` へ排出する。
- 本体の DPI context を子に設定し、`hello` で context とシステム DPI を照合する。不一致は拒否する。`run-started` の `BotVersion` は実行した版のフォルダー。
- `install-bot.ps1` は別の版フォルダーへ publish し、設定読込と protocol の自己試験後に `current.txt` を置き換える。`previous.txt` は戻し先。本体と導入処理が共有する短い Mutex で current の読取・実行印作成・版削除の競合を防ぐ。開いている実行印と戻し先の版は削除しない。

## 検証

focused test は関連35件と Architecture の追記2件が成功し、切断処理の修理後は Bot の focused test 19件が成功した。入力中の問い合わせ、矢印不動の例外、protocol / DPI 不一致、片方の切断、EOF による接続待ち・両応答待ちの停止、受付済み入力の完了前の番の保持、未受付入力の拒否、応答消失時の無再送、次の実行、専有接続の一括解放なしの終了、実行印の削除拒否を確認した。

最終の通し試験は Host 898件・Architecture 11件がすべて成功、スキップ0件。新規試験は Host 20件・Architecture 2件。Host は初回897件成功後、追加の focused test で「入力中に入力用パイプだけが切れると、問い合わせ受付を閉じるまで入力完了を待つ」ことを再現した。先読みは受付と分け、切断を検知して受付を閉じつつ有限入力を完了させるよう修理し、Host の通し試験を1回再実行した。Architecture の通し試験は1回。Capture.Tests は実行していない。

`install-bot.ps1` は構文エラー0件。導入スクリプトと `bot-worker --self-test` の実process実行、publish は行っていない。稼働中のアプリ・Bot・ゲーム・実機と、元の worktree のファイルには触れていない。

## 実機受入は未確認

設計 §5 の段階1の完了条件6項目は、すべて未確認。

1. 本体 PID を変えずに Bot を更新でき、`run-started` の版が変わり、その間も G13 のキーが効くこと。
2. Bot 実行中の G13 の押下で `user-input-paused` になること。
3. Bot の強制終了で、本体の異常表示・通知・番の解放・キー残留なしが成立すること。
4. 本体を通常終了すると子も終わること。
5. 本体の強制終了でも子が残らないこと。
6. 停止直後に次の実行を始められること。

設計 §6 の実測も、すべて未確認。クリック中の fast path の p99（受入10ms以下）と最大値、パイプ往復遅延、開始から `run-started` までの時間、100%以外の表示倍率での両 process の座標、管理者権限のゲームでの手入力監視、窓を表示しない子での WGC / OCR を確認する必要がある。実processの終了・Job Object・期限超過 kill は、今回の fake / パイプ試験から実測済みとは扱わない。

設計の責務・操作・終了順序は維持した。5秒の期限、Job 登録後の起動合図、DPI context の設定、版管理の短い排他と削除不能の実行印は、設計を具体化した実装上の選択。新しいオーナー裁定が必要な仕様変更は行っていない。
