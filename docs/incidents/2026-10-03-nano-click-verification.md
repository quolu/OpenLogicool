# NIKKEのNanoクリック検証と、実行開始時の停止

## 結論

製品のNano USB HID経路でロビーからアークへ入るクリックは確認済み。Computer UseのSendInputクリックが反映されなかった結果を、製品入力経路の失敗として扱った検証方法を訂正した。

製品UIの実行開始を止めていた前面化とCodex通信を修理し、開発版へ導入した。GUIからNanoのクリックでアーク→ロビー、実画面の遷移、1手順マクロの保存、完了表示まで確認済み。即時失敗を遅延した開始表示が上書きする欠陥も修理した。

別々に学習した「アークを開く」と「ロビーへ戻る」の統合障害も解決済み。保存観測から画面identityを再利用・訂正する修理を開発版へ導入し、GUIで2手順の統合保存、別process再起動後もAI 0・版1不変のNano往復を確認した。連続性検証は保持している。詳細は[統合障害の記録](2026-10-03-macro-scene-identity.md)。

## 実測

| 実行 | 入口と結果 |
| --- | --- |
| `run-20261003-092433-472` | 製品UIの「AIに作ってもらう」。`CodexAppServerClient.RunAsync`の受信JSON解析で`JsonReaderException`。byte位置884、`'e' is invalid after a value`。result.jsonとゲーム画像の保存なし |
| `run-20261003-093710-144` | 製品の`CodexPurposeMacroExecutionEngine`を診断用実行から呼出し。transportは受信行を保存してそのまま返す。既存Nano・画面取得・比較・route保存を使用。ロビー→アークを実画面で確認。クリック1回、tool 5回、route版2・1 step、完了、tool error 0 |
| `run-20261003-093842-384` | 製品UIから「ロビーへ戻る」。`WindowsTaskbarNanoWindowActivator.ActivateFromTaskbar`の前面化確認で停止。ゲーム内の戻るクリックより前。result.jsonとゲーム画像の保存なし |
| `run-20261003-101914-198` | 前面化修理後、GUIからの再実行でCodex応答の文字化けとJSON破損を再現。未加工受信行を保存。失敗表示が開始表示に戻らないことも確認 |
| `run-20261003-102423-739` | 通信修理を開発版へ導入し、通常起動したGUIから「ロビーへ戻る」。Nanoクリック1回、tool 4回、版2・1手順、`Completed=true`、tool error 0。NIKKE実画面のロビーとGUI完了・保存一覧が一致 |

前面化のエラー文は「taskbar buttonをNano clickしてもtarget windowがforegroundになりませんでした。」。Hostの保存状態も`Faulted`、`CanStart=true`、`CanStop=false`を確認した。

診断実行で保存したCodex受信261件は全件有効だったが、GUIでは後述の文字コード不一致を再現できた。無視・再送・別入力経路への切替は追加していない。

## 原因と修理

### 同名ランチャーの誤選択

旧adapterはタスクバーの表示名を照合し、カーソルに近い候補をクリックしていた。本体とランチャーが両方「NIKKE」と表示され、実測ではランチャーが前面に出た。2秒後も本体にならず、250msの確認時刻が短いという仮説は棄却した。

`WindowsGameTargetLocator`で既存の`QueryFullProcessImageName`経路から対象EXEのパスを取得し、`WindowsTaskbarNanoWindowActivator`でUI Automationの`AutomationId`にある`Appid: <EXEパス>`へ完全一致させた。表示名へのfallbackは置かない。

同じカーソル位置・同名候補で、修理後は本体のタスクバーボタンをNanoで1回クリックし、本体PID 7892／window 461232が前面になった。証拠は`probe-output/nano-taskbar-selection-{before,after}-20261003.json`。

### GUI起動時のUTF-8誤読

GUIの受信原文にある日本語は、元のUTF-8バイト列をCP932で復号した結果と完全一致した。文字列末尾の引用符も壊れ、byte位置647でJSON解析が失敗した。

コンソールを持たないWindows native診断プログラムから、製品の`CreateStartInfo`とPowerShell 7 wrapperを使い、既知のUTF-8 JSONを返す小さなnative processを起動した。修理前はコンソールcode page 0・明示encodingなしで文字列不一致、JSON不正。標準入力・出力・エラーをBOMなしのUTF-8へ明示した後は、同じ条件で原文と完全一致、JSON有効になった。証拠は`probe-output/codex-gui-encoding-{before,after}-20261003.json`。

`CodexAppServerClient`は不正JSONを読み飛ばさず、受信行をrun内の`codex-invalid-response.txt`へ保存して停止する。これは原因確認のための診断であり、異常な応答の補正はしない。受信履歴・画像を含む実原文は非公開のローカルrunに保持し、公開repoへ含めない。

### 失敗表示の上書き

`Progress<T>`が同じUI threadの開始通知までqueueへ入れ、同期失敗後にその通知がエラー表示を上書きしていた。`MacroAutomationPanel`の通知を既存`OnStateChanged`へ直接渡し、UI threadでは同期表示、別threadだけDispatcherで表示するよう修理した。

公開UIの再現テストは修理前に失敗、修理後に成功。実GUIでも通信失敗の表示が残り、開始・再生は有効、停止は無効になった。

## 導入と検証

旧アプリを通常終了し、正規`scripts/install-development-app.ps1`で開発版へ導入。標準Launcherから別Host processを起動し、Game Operatorの「AIに作ってもらう」で確認した。Computer Useは製品UIの操作とゲーム画面の読み取りだけに使用し、ゲームへの入力は製品Nano runtimeが行った。

関連テストはHost 313件、Desktop 104件、計417件すべて成功。対象は同名ボタン選択3件、即時失敗の表示保持、不正応答の原文保存と停止、既存の関連機能。全体regressionはこの個別修理では再実行していない。

続いてGUIで保存済み単独マクロをAI監視なしに切り替え、「アークを開く」→「ロビーへ戻る」をそれぞれ再生した。どちらもNano入力と実画面の遷移、GUIの「目的を完了しました。／Moved／AI 0回／版2」を確認した。両routeはrevision総数2、最新版2のままで、再生による追記なし。「アークを開く」は学習した診断processと別のHost processでの再生。2つを統合したマクロの再生ではない。結果の索引は`probe-output/nano-product-ui-live-20261003.json`。

Computer Useの背後window観測には古いtreeと画像が返った。再生終了の判定はゲーム画面と、前面に出した製品画面の表示を照合して行った。「開始しています」が残っているという途中判断は、この古い観測による誤認だった。

Game Operatorは通常割当のWindows出力設定と独立してNano sessionを解決する。今回は`CodexPurposeMacroExecutionEngine`→製品dynamic tools→既存Nano入力runtimeの経路で確認した。通常割当の出力設定は変更していない。

## 証拠の所在と範囲

実行原本はローカルの`%LOCALAPPDATA%/OpenLogicool/game-agents/nikke-157eacb84ec0/runs/`に保持する。診断の受信行はrepoの非公開artifact領域`artifacts/diagnostics-tools/macro-trace-evidence/`に保持する。Microsoftの.NET診断ツールで調べた対象はOpenLogicool Hostだけで、NIKKEのメモリには触れていない。

製品入力の成功と、利用者による記録・全工程の受入は分ける。この実測でPhase 14 Exit、配布完了、利用者デモからの2 stepマクロ成立は宣言しない。

## 統合障害の解決

開始障害の原因調査・修理・導入・GUI実測は完了。統合拒否では、初回のNovelと次回のKnown、state候補とOCR集合の差でsignatureが分かれたことを確認した。同じアークの保存画像の平均輝度差は0.046875だった。画面構造の所有モジュールで保存観測から同一性を解決し、正規mergeを追記してGUI統合を成立させた。原本・旧route・旧eventは不変。詳細と2回のAIなしNano往復は[統合障害の記録](2026-10-03-macro-scene-identity.md)にまとめた。

外部仕様の根拠は[公式App Server仕様](https://learn.chatgpt.com/docs/app-server)と[Microsoftの標準出力encoding仕様](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.processstartinfo.standardoutputencoding?view=net-10.0)。調査知識は[UTF-8のGUI通信検証](../../rag/openlogicool/codex-gui-stdio-encoding-2026-10-03.md)へ保存した。
