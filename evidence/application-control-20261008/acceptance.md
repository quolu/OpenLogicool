# アプリ操作CLIと手入力一時停止の受入記録

2026-10-08〜09、Windows native、通常の開発版Launcherと利用中DBで確認した。ゲームへの入力はNano経路を維持する。

## CLIの確認結果

| 対象 | 実測結果 | 記録 |
|---|---|---|
| アプリ起動・終了 | 停止状態からCLIの要求1回でLauncher起動・操作実行。通常終了はプロセス終了まで待つ。更新後も往復成立 | `open-final.json`、`close-final.json` |
| Bot開始・停止 | 開始結果は手入力一時停止中。停止結果は停止済み。最後の試行は観測1回、進行・回復品送出0回 | `bot-start-final.json`、`bot-stop-final.json` |
| GUIの状態取得 | Bot画面を前面化せずPNG保存。一時停止と停止済みの表示を目視確認 | `bot-paused.png`、`bot-stopped.png` |
| GUI操作の公開範囲 | 82操作。GUIが受け取る12の操作interfaceと、割当文書の公開編集操作を機械照合 | `operations.json`、`ApplicationControlTests` |
| 割当編集・検証 | 空文書を編集APIで作成し、複合JSONをファイル経由で検証。2プロファイル、検証成功。保存は行わない | `editor-draft.json`、`compile.json` |
| 設定・記録等の取得 | 割当読出し、マクロ対象、記録一覧、出力設定、本体状態、LCD文字変換が完了 | `workspace.json`、`macro-targets.json`、`recordings.json`、`serial-state.json`、`onboard-state.json`、`lcd-text.json` |
| 実行管理 | 受付時の実行IDから結果を取得。完了済み処理への中止要求も成功応答 | `async-job.json`、`job-result.json`、`cancel-completed.json` |
| 誤った要求 | DB不一致・未知操作は実行前拒否。必須引数不足は明示エラー・終了コード2 | `db-mismatch.json`、`unknown-operation.json`、`invalid-argument.json` |

長い処理の進捗・中止と、アプリ終了時の回収は、実パイプとキャンセル可能な試験用処理で確認した。今回、ゲームの新規録画・マクロ再生・本体書込みを全操作ぶん実機再実行してはいない。公開範囲の完全性と、既存GUI処理を使うことを試験で保証する。MCPサーバーは追加せず、CLIを提供した。

## 手入力一時停止

先行実機実行`20261008-230708-312-b45fd0ca`の入力・状態イベントを、原本から変更せず抜粋して`user-input-events.jsonl`へ保存した。

- 一時停止→再開の23区間すべてで、進行キー・進行クリック・回復品送出は0回。
- 最初の区間では全解放後3,005msで再開。先行確認の5区間は3,005〜3,084msで再開した。
- Nanoイベントだけが増え、手入力イベントが不変で、前後とも一時停止していない区間を139件確認した。Nano自己入力での一時停止は発生していない。
- 新しいCLIからの開始時にも`UserPaused`を返し、同じ表示をPNGで確認した。

集計は`user-input-pause-summary.json`と`nano-exclusion-summary.json`。別の単発診断`nano-input-probe.json`は観測中に手入力が続き、診断送出0回だった。この診断をNano除外の成立根拠には使っていない。

確認事項があっても進行規則と監視を継続する先行修理は、`../bot-app-control-20261008/continue-rules-summary.json`に、未確認食事の追加使用0回、以後の進行入力5回として記録した。

## 検証と修理

全22プロジェクト・1,579件が成功。その後、終了結果の返信とアプリ終了の競合を試験で再現した。受付時の古い状態を返す間に処理が完了すると、完了結果を返す前に終了処理が走る原因だった。実際に返信した状態が完了である時だけ終了するよう修理し、関連5件が成功。完了結果の二重読出しでも終了処理は1回だけ。修理後の開発版でCLIの終了・再起動・Bot開始・停止・画像保存を再確認した。

開発版Host DLLとRelease成果物のSHA-256は一致: `CCD83E52F8F1E18581A151D4C600D376047A2A50F6AB3155735ED345A0B1E075`。

最後はアプリを起動したまま、Bot・記録・マクロを停止状態にした。既存DB・記録原本・マクロ版・キー割当の書換えは、このCLI試験では行わない。
