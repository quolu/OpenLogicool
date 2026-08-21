# t07-data-flow-controls 証跡

## 実装

- `GameOperatorDataControls` に image 保存、cloud evidence crop 送信、削除、provider 状態、cost 上限を確認・制御する pure な入口を追加した。
- 既定は image 保存 OFF、cloud OFF、provider 未選定、cost 0 USD。provider 未選定のまま cloud を許可しても送信を開始できない。
- cloud 対象は明示した evidence crop に限定し、screen image と secret を含むそれ以外のデータ種別を拒否する。cost 上限も送信前に確認する。
- `DiagnosticBundle`、provider/network、capture、既存の data-flow 実装は変更・再実装していない。既定 diagnostic bundle の screen / secret 除外は表示状態として確認するだけである。

## focused test

`dotnet test tests/OpenLogicool.Host.Tests/OpenLogicool.Host.Tests.csproj --filter FullyQualifiedName~GameOperatorDataControlsTests`

- 結果: 4 件合格、失敗 0（0.4803 秒）。
- 既定 OFF、provider 未選定時の cloud 抑止、evidence crop の cost 境界、screen / secret の cloud 拒否、保存済み image の削除 authorization を確認する。
