# Nano無応答の復旧と通知担当の引き継ぎ

2026-10-10、Windows native。前任の未保存変更を継続して検証した。

## 確認済み

- 前任はBotのHeartbeat sequence 11378のTimeout後、fresh HELLOにも応答しないことを確認した。接続を回収し、Nano一台のexact PnP機器IDだけをWindows標準機能で再起動した。READY 1.1.3・全解放24ms成功後、Botを新しい実行で再開した。`connection-test.json`、`bot-restarted.json`を参照。
- `SerialPortFrameExchange`へ送信／受信工程、経過時間、読取済みbyte数、直前buffer数を追加した。期限と再送条件は変更していない。導入後にもfresh HELLO無応答が発生し、受信2021.1ms・読取0byte・直前buffer0byteを記録できた。二度目の限定PnP再起動後はREADY・全解放25msに成功した。`connection-after-close.json`、`connection-second-restart.json`を参照。
- 利用者が以前の会話に戻って新しい作業を依頼した場合の`assistant attach --takeover`を追加した。通常の退役会話の再登録拒否、未処理案件の保持、担当の再取得、前担当からの操作拒否を試験した。前任の引き継ぎ結果は`notification-owner.json`に保存済み。
- 今回のThroughline継続では通常の`assistant attach`で通知先を現在の会話へ変更した。ルピーへ旧通話から移動先を通知し、新通話へ未実測事項を渡して旧通話を閉じた。未決・保留のApproval Box申請はなかった。
- 保存証拠を読み、既に完了していた技巧優先3段階、回復品の購入・登録、実価格の判断待ちの3案件をclaim／resolveした。ゲーム操作と購入は繰り返していない。`previous-completed-incidents.json`を参照。
- 前任のfocused testは47件成功。今回の通知・読取り関連のfocused testは19件成功。最終通し試験は22プロジェクト・1,656件成功。`focused-tests.txt`、`final-tests.txt`を参照。
- 開発版Host DLLのSHA-256はRelease成果物と一致した。前任による正規installerの完了記録は`install.log`、今回の照合は`installed-hashes.json`を参照。

## 未確認と継続位置

- HELLO無応答の根本原因は未解明。機器再起動による復旧と、入力なし200msで旧firmwareがSequenceViolationになる既知の再現欠陥を混同しない。
- firmwareの修正版は未導入。実機は1.1.3。手動RST依頼は取り下げ済みで、今回も基板の操作を頼んでいない。
- 前任が長時間起動警告からゲームをNanoで通常終了した。今回、公式サイトの「ゲームスタート」を一度実行し、NexonLauncher64の起動を確認した。ゲーム本体の起動と復帰画面は未確認。
- 続くChrome画面の読取りは、Computer Useが現在のブラウザURLを確定できず安全判定不能として停止した。以後の画面操作・Nano入力・Bot再開は行っていない。BotはStopped。
- 長時間起動警告に重なったボーナス案件はclaimedのまま保持した。過去画像の選択肢を現在の選択肢として申請しない。次は起動結果と現在の画面を確認し、必要な選択を扱ってBotを再開する。

通知先・通話・未処理案件・次の操作は`docs/bot-assistance-handoff.json`へ保存した。録画原本、マクロ、割当は編集していない。
