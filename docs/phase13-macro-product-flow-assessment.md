# Phase 13 Macro Product Flow Exit判定（2026-08-29）

## 結論

**Phase 13 Exitは成立した。** 製品実装、実gameのAI監視付き作成・修復、AI 0再現、実データ合成、G13／G600割当経路、再起動復元、Nano長時間出力がすべて成立し、mainへ配布可能な状態である。

Game Operator実windowからの手動入力1:1確認は、オーナー裁定「リモート中はUIをスキップし、後日1:1確認」によりExit後UX確認へ移した。UIのpublic intent、実SQLite、macro token、G13／G600 bindingは自動試験と実DBコピーで成立している。NIKKE shop購入routeのAI 0は日次／週次reset後確認とし、AI 0 mode自体は日課routeと実composed routeで実機成立している。

## 成立

1. Learning Route revisionを唯一のmacro正本とし、別schema／別DB／Probe subprocessを作っていない。
2. Game Operatorの既存TabControlへ「マクロ」tabを追加し、goal作成、catalog、AI監視あり／なし、停止、順序付き合成をHost intentだけで操作する。
2a. マクロ対象game profileはアプリ側へ永続化し、現在は`nikke`に固定した。request側の別game指定は拒否し、catalog／合成もNIKKEだけに絞る。
3. 既存Input Studioの3ペイン／G13・G600図／layer／保存導線を維持し、右Inspectorへ「マクロを選ぶ」だけを追加した。
4. Macro tokenは既存Semantic ActionとWorkspace revisionへ保存し、G13／G600の既存bindingへ割り当てる。修復後のlatest routeを追従する。
5. AI監視なしは保存actionだけを実行し、AI providerへ構造的に到達しない。非遷移時は停止し、route／Structureを更新しない。durable outcome evidenceは保持する。
6. AI監視ありは保存actionを先に実行し、10秒非遷移後だけ同じstepをAI再探索する。成功時は正常stepと旧revisionを残して失敗stepだけを新版へ差し替える。
7. 複数macroはsourceを変更せず、同一game／environmentのedge列を選択順に連結する。edgeごとのclick／hover／key／scroll／drag parameterを保持して再生する。
8. G13／G600 fast pathはbutton down一回を有界queueへ`TryEnqueue`するだけで、AI／capture／SQLite／UIを待たない。通常key／mouse／finite sequenceのownershipとreleaseを維持する。
9. UI手動起動と物理button起動は同じHost coordinatorを通る。物理起動faultはGame Operatorへ`Faulted` stateとして通知する。
10. resident Serial HID中は同じNano sessionを借用し、二重COM openをしない。SQLite portは各同期操作でfresh connectionを所有し、awaitを跨いで接続をthread移動しない。

## 実測

- Windows Input Studio実window: exit 0、既存配置維持を[screenshot](../evidence/macro-product-flow/input-studio-ui.png)で確認。
- NIKKE「アークを開く」:
  - AI監視なし: `Completed`、Moved、AI 0、route revision 1。
  - AI監視あり正常step: `Completed`、Moved、AI 0、route revision 1。
  - 実行後はNano Escでホームへ復帰。
- 機械証拠: [AI監視なし](../evidence/macro-product-flow/nikke-ai-free.json)、[AI監視あり正常](../evidence/macro-product-flow/nikke-ai-monitored-normal.json)。Nano-onlyは実装経路とCLI投影による強い推定で、macro snapshot内の独立dispatch receiptはない。
- NIKKE「日課の未完了項目と進捗を最下端まで取得」: アプリ内CodexのAI監視付き修復後、revision 69・9 stepを別runのAI 0で全step Moved・revision不変・Completed。証拠は[AI監視付き修復とAI 0再現](../evidence/macro-product-flow/ai-monitored-ai-zero-replay-20260828.md)。
- NIKKE実購入goal: 一般ショップ無料品、費用0更新後の無料品、ボディラベルSALE品、キャッシュショップ赤ポチ付き無料品を20 step・revision 21でCompleted。有償通貨・ジュエル・現金・非無料品0。証拠は[NIKKE shop purchase macro](../evidence/macro-product-flow/nikke-shop-purchase-macro-20260829.md)。
- 実データ合成: ロビー→MISSIONデイリー1 stepとMISSION→ロビー1 stepを`macro:composed:00b2081f045145b58b4d92819a26b762`へ統合。別Host processのAI 0で2/2 Moved・revision 1不変・Completed。
- G13／G600割当: composed macro latest tokenを実DBコピー上のworkspace `phase13-composed-acceptance`へG13 G1／G600 G9として保存。revision 1、G13/G600 profile生成、別process再open exportでtoken・2 binding一致。
- Nano firmware 1.1.2: releasePending 1秒でwatchdog自己再起動、native recovery test、公式build、wedgeした1.1.1から1200-baud verify付きflash、firmware 1.1.2 handshake成立。
- Nano firmware 1.1.3 follow-up: USB／CDC call内blockでは1.1.2のsoftware timerへ戻れない欠陥を実走再現し、main loop全体を2秒のAVR hardware watchdogで監視した。Hostは`DispatchFailed`をCodex tool successとして返さず、同じrunの後続actionと偽Completedを拒否する。verify付きflash、100 session×2、shop monitored長時間run後のhandshakeが成立。
- shop reset後のAI監視run: 19 step、AI 1、route revision 21→25、Completed。無料クレジット60,000、費用0更新、無料コアダスト30、40% SALEコンソール10個、デイリー無料パックを処理し、ウィークリー無料パックは開始時点SOLD OUT。有償通貨・現金・非無料商品・募集・戦闘0。
- fake＋実SQLite scenario: 作成→AI 0→失敗step修復→2macro合成→G13 G1／G600 G9割当→DB再open復元を同じpublic intent経路で確認。
- 関連test: Perception 32、Exploration 53、Input 157、Host 252、Desktop 98、Playbooks 164、Persistence 50、architecture 8、合計814件green。firmware native testとArduino buildもgreen。
- 最終full regression: 22 test project・1258件green・failed 0・skipped 0。実装完了後に一回だけ実行した。
- firmware 1.1.3／terminal tool failure follow-up最終full regression: 22 test project・1259件green・failed 0・skipped 0。follow-up修正完了後に一回だけ実行した。
- Peertable read-only再反証: 修正後コードにP0／P1なし。受入証拠の未確認2点は上記どおり分離。

## Exit後の確認

1. Game Operator実windowで、作成→G13／G600割当→2mode→修復→合成→再起動再生を手動入力で1:1確認する。
2. NIKKE shopの次回日次reset後、revision 25の購入routeをAI 0で再現する。同reset分はAI監視runで購入済み。

公開claimは`Game Operator Preview`のまま維持する。一般game自律操作、Verified Autonomous Playbook、課金／消費／戦闘macroを含めない。
