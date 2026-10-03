# NIKKEのNanoクリック検証と、実行開始時の停止

## 結論

製品のNano USB HID経路でロビーからアークへ入るクリックは確認済み。Computer UseのSendInputクリックが反映されなかった結果を、製品入力経路の失敗として扱った検証方法を訂正した。

製品UIからの往復は未成立。Codex通信のJSON解析エラーと、Nanoによるタスクバー前面化の失敗を別々に観測した。いずれも原因は未特定で、本体の修理・開発版更新は行っていない。

## 実測

| 実行 | 入口と結果 |
| --- | --- |
| `run-20261003-092433-472` | 製品UIの「AIに作ってもらう」。`CodexAppServerClient.RunAsync`の受信JSON解析で`JsonReaderException`。byte位置884、`'e' is invalid after a value`。result.jsonとゲーム画像の保存なし |
| `run-20261003-093710-144` | 製品の`CodexPurposeMacroExecutionEngine`を診断用実行から呼出し。transportは受信行を保存してそのまま返す。既存Nano・画面取得・比較・route保存を使用。ロビー→アークを実画面で確認。クリック1回、tool 5回、route版2・1 step、完了、tool error 0 |
| `run-20261003-093842-384` | 製品UIから「ロビーへ戻る」。`WindowsTaskbarNanoWindowActivator.ActivateFromTaskbar`の前面化確認で停止。ゲーム内の戻るクリックより前。result.jsonとゲーム画像の保存なし |

前面化のエラー文は「taskbar buttonをNano clickしてもtarget windowがforegroundになりませんでした。」。Hostの保存状態も`Faulted`、`CanStart=true`、`CanStop=false`を確認した。

診断実行で保存したCodex受信261件は、`System.Text.Json.JsonDocument.Parse`で全件有効だった。最初のJSONエラーはこの実行では再現しなかった。無視・再送・別入力経路への切替は追加していない。

Game Operatorは通常割当のWindows出力設定と独立してNano sessionを解決する。今回は`CodexPurposeMacroExecutionEngine`→製品dynamic tools→既存Nano入力runtimeの経路で確認した。通常割当の出力設定は変更していない。

## 証拠の所在と範囲

実行原本はローカルの`%LOCALAPPDATA%/OpenLogicool/game-agents/nikke-157eacb84ec0/runs/`に保持する。診断の受信行はrepoの非公開artifact領域`artifacts/diagnostics-tools/macro-trace-evidence/`に保持する。Microsoftの.NET診断ツールで調べた対象はOpenLogicool Hostだけで、NIKKEのメモリには触れていない。

製品入力の成功と、利用者による記録・製品UIの受入は分ける。この実測でPhase 14 Exit、配布完了、利用者デモからの2 stepマクロ成立は宣言しない。本体コード変更なしのため、機能テストの再実行と開発版の再導入は行っていない。

## 次の調査

前面化について、実行時に選んだタスクバーボタン・座標・Nano送出結果・前面windowの時系列を製品adapterで観測し、選択の誤りと前面化完了の観測時刻を切り分ける。先のJSONエラーは、再現時の未加工受信行を保存して発生箇所を特定する。[公式のApp Server仕様](https://learn.chatgpt.com/docs/app-server)はstdioをJSONLとして定義しており、不正な行を黙って読み飛ばす修理はしない。
