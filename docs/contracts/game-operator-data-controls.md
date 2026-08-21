# Game Operator data flow controls 契約

`GameOperatorDataControls` は image 保存、cloud 送信、削除、provider、cost を利用者が確認・制御するための pure な入口である。副作用、provider 選定、network 接続、diagnostic bundle の生成・削除は行わない。

- 既定では full-screen image の保存、cloud evidence-crop 送信、provider 指定、cost 上限はすべて OFF / 未選定 / 0 USD である。
- image の保存は `SaveScreenImages` を明示した時だけ許可する。既存の capture 経路の実装は変更しない。
- cloud は explicit evidence crop だけを候補にし、利用者の送信許可、選定済み provider、cost 上限内のすべてがそろうまで開始しない。provider 未選定では送信許可を ON にしても開始しない。
- screen image、journal、prompt、secret はこの cloud 送信口の対象外である。provider の選定や転送実装をこの契約から導入しない。
- 保存済み image の削除は、保存の再実装や diagnostic bundle への副作用を伴わない authorization として提示する。
- 既存 `DiagnosticBundle` の既定 bundle は screen image と secret を含めない。この契約は既存 bundle の生成・削除を再実装しない。
