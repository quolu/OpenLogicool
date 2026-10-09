# 起動中アプリのCLI操作

配布フォルダーの`OpenLogicool.Host.exe`から、起動中のアプリと同じ操作を呼べる。PowerShell 7で実行する。アプリが起動していなければ、操作要求時に同梱Launcherで通常起動し、操作APIへの接続を確認してから実行する。`app status`と`app close`は、停止中のアプリを起動しない。

```powershell
$cli = './artifacts/development/OpenLogicool/OpenLogicool.Host.exe'
& $cli app status
& $cli app open --view bot
& $cli bot list
& $cli bot start mabinogi-mobile
& $cli bot status
& $cli bot stop
& $cli device list --wait
& $cli app snapshot --view bot --path ./bot.png
& $cli app close
```

`bot start`は、受付だけで完了せず、開始処理を終えて実行中・手入力一時停止中などになった状態を返す。`bot stop`は入力と監視の終了を待つ。`app close`はBotと実行中の操作を回収し、アプリのプロセス終了まで待つ。未保存の画面編集がある時は終了を拒否する。破棄する場合だけ`--discard-unsaved-changes true`を指定する。

`app open`と`app snapshot`の`--view`は`input-studio`、`bot`、`macro`、`recording`、`explorer`、`learning`、`research`。画像保存はゲームを前面から外さず、アプリ自身のWPF表示をPNGにする。

## 全操作の一覧と引数

```powershell
& $cli control operations --out ./operations.json
& $cli control invoke workspace.load-document --application-full-path '*' --wait
& $cli control invoke macro.list-targets --wait
& $cli control invoke recording.list-sessions --wait
& $cli control invoke serial.load --wait
& $cli control invoke lcd.from-text --text '表示する文字' --wait
& $cli control invoke onboard.query-state --wait
```

一覧はGUIが使う操作interfaceと割当編集処理から生成する。各操作のID、利用可否・理由、必須引数、JSON Schema、結果型を含む。Bot、記録、マクロ、割当編集・保存・undo、通常入力への適用、出力設定、LCD、本体書込み、Web調査、構造探索、学習した操作、教師付き実行をこの入口で扱う。起動モードにより利用できない操作は、一覧に理由を表示し、実行もエラーとして返す。

引数名はinterfaceの引数名。`--application-full-path`のような指定を`applicationFullPath`へ変換する。複合データは`--json-file`で渡す。

```powershell
# 引数JSONの形はcontrol operationsが返すSchemaで確認する。
& $cli control invoke workspace.compile --json-file ./compile-arguments.json --wait
& $cli control invoke workspace.save --json-file ./save-arguments.json --wait
& $cli control invoke macro.confirm-step --json-file ./confirmation-arguments.json --wait
```

`workspace.save`と`workspace.undo`は、GUIと同じ保存処理の後で通常入力にも適用する。編集の各操作は`editor.*`へ文書を渡し、返された文書を次の編集・検証・保存へ渡す。CLIで画面内の未保存文書を直接書き換えない。

既存の`macro list|play|target|create|compose|token`、`onboard`などのCLIも残る。起動中アプリの共有操作を使う時は`control invoke macro.play`、`control invoke onboard.apply`のように操作IDを指定する。

## 長い処理と結果

`control invoke`は実行IDと状態をすぐ返す。`--wait`で終了まで待てる。`app`と`bot`は既定で待つ。

```powershell
& $cli control invoke research.start --json-file ./research-arguments.json
& $cli control jobs
& $cli control job <実行ID>
& $cli control cancel <実行ID>
```

状態は`queued`、`running`、`completed`、`faulted`、`cancelled`。進捗、結果、エラー、中止要求の有無をJSONで返す。中止要求と終了は区別し、実際の終了は`control job`で確認する。CLIだけをCtrl+Cで終了しても、既に受け付けたアプリ内の処理は続く。実行IDで中止するか、その機能の停止操作を呼ぶ。

応答は`success`、`value`、`error`、`version`を持つ。完了結果は`value.result`。失敗時の終了コードは2、成功時は0。`--out`は応答JSONの保存先で、PNGの保存先は`app snapshot --path`。相対パスはCLIを呼んだフォルダーから解決する。

`--db`は起動先・接続先のDBを指定する。起動中アプリのDBと異なる場合は実行前に拒否する。通信はWindowsの現在の利用者だけが接続できる名前付きパイプで、ネットワークには公開しない。MCPサーバーの追加は行わず、今回はこのCLIを共通の操作入口とする。

## 手入力による一時停止

Botは手元のキーボード・マウスとリモート操作を検出すると、前面化と入力送出を一時停止する。画面観測は続け、全キー・ボタンの解放後3秒間入力がなければ再開する。Nanoの入力はUSBの識別情報で除外する。状態は`bot status`とBot画面の「手入力で一時停止中」で確認できる。

実機確認は[受入記録](../evidence/application-control-20261008/acceptance.md)を参照。

## Botから担当AIへの支援依頼

`assistant attach|status|claim|resolve`で現在の会話を登録し、詰まりと異常終了の案件を引き継ぐ。[Bot支援の運用](bot-assistance-workflow.md)に接続条件・配達状態・Throughline後の手順を記載している。定期監視は使わず、Botから詰まりイベントで通知する。
