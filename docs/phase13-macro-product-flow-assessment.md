# Phase 13 Macro Product Flow 判定（2026-08-26）

## 結論

製品実装は成立し、mainへ配布可能な状態である。Phase 13 Exitそのものは未宣言とする。

未確認は、Game Operator実windowから全journeyを一巡する目視証拠と、NIKKE liveで保存actionの非遷移を故意に発生させAI修復を実発動する証拠の2点である。コード、public intent、fake＋実SQLite、Windows Input Studio、NIKKE正常再生の成立を、この2点へ読み替えない。

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
- fake＋実SQLite scenario: 作成→AI 0→失敗step修復→2macro合成→G13 G1／G600 G9割当→DB再open復元を同じpublic intent経路で確認。
- 関連test: Host 226、Desktop 97、Input 156、Exploration 50、architecture 8、すべてgreen。
- 最終full regression: 22 test project・1226件green・failed 0・skipped 0。一回だけ実行。
- Peertable read-only再反証: 修正後コードにP0／P1なし。受入証拠の未確認2点は上記どおり分離。

## 未確認

1. Game Operator実windowで、作成→G13／G600割当→2mode→修復→合成→再起動再生の全操作を一巡した目視証拠。
2. NIKKE liveで保存actionを10秒非遷移にし、AI修復が発動してroute新版へ更新される証拠。

公開claimは`Game Operator Preview`のまま維持する。一般game自律操作、Verified Autonomous Playbook、課金／消費／戦闘macroを含めない。
