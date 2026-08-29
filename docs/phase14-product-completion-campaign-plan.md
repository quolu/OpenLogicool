# Phase 14 Product Completion／操作デモ記録 campaign

状態: Active

工程正本: Lattice plan `phase14-product-completion`

## 目的

OpenLogicoolを、利用者がGame Operatorだけで目的入力、操作デモ記録、AI監視付き検証・修復、AI 0再生、複数macro統合、G13／G600割当まで一巡できるアプリとして完成させる。

操作デモ記録は新しい再生器を作らない。利用者の実操作とbefore／afterをimmutable evidenceとして保存し、そこから既存Learning Routeの候補を作る。候補routeはPhase 13と同じAI監視あり／なし、10基盤、Nano、10秒Compare、失敗stepだけのappend-only修復を通る。

## 成功条件

1. NIKKEをforegroundにして明示的に記録開始した時だけ、mouse、keyboard、G13、G600の有限操作と時刻を取得する。他appへ切り替わった間は記録を一時停止する。
2. 各操作はcurrent window／frame／transform、正規化座標またはkey／device control、操作前後のWGC scene、10秒Compare、Moved／Stayed／Undeterminedと関連付けてappend-only保存する。
3. 操作デモ原本は修正しない。AIはgoalと遷移を照合して採用stepをLearning Routeへ投影し、寄り道や非遷移を原本から削除せずroute側で不採用にする。
4. 記録から作った候補routeをAI監視ありで再生し、保存action優先、非遷移stepだけAI修復、正常stepと旧revision維持を成立させる。
5. 別process再起動後に同routeをAI 0で再生し、route revision不変で完了する。
6. Game Operatorにgoal、記録開始／停止、記録session、記録からmacro作成、AI監視あり／なし再生、進捗、停止理由を利用者語彙で表示する。
7. 既存Input StudioのG13／G600配置・編集・保存を壊さず、完成macroの選択と割当だけを既存Inspectorへ接続する。
8. 複数macro統合、button down一回のqueue、SQLite再open、game別相対workspace、Codex subscription入口を既存Phase 13経路のまま再利用する。
9. Windows nativeの実アプリで記録→候補route→AI監視修復→AI 0→統合・割当を一巡し、開発版install後も同じ結果を得る。

## 設計契約

### 三層を分離する

- `Demonstration Session`: 利用者のgoal、対象game、開始・停止、focus区間、操作、before／after、evidenceを持つimmutable原本。
- `Learning Route`: Demonstration Sessionから導出する修復可能なedge列。修復は新revisionだけを作る。
- `Product Macro`: routeの再生mode、統合、G13／G600割当を所有する既存製品面。

Demonstration Sessionをmacroとして直接再生しない。座標列だけのblind replayを作らない。

### 入力取得境界

- mouse／keyboardのOS取得はWindows環境別adapterだけが所有する。共通contractは操作eventとlifecycleだけを持つ。
- G13／G600は既存Raw Input／device adapterのedgeをobserverへfan-outし、fast pathを待たせない。
- 記録中はmacro再生を開始できず、再生中は記録を開始できない。Nano出力を利用者デモとして自己記録しない。
- foregroundが対象gameでなくなった区間はpause eventとして保存し、他appの画面・座標・key文字列を保存しない。
- passwordや他appの入力を集める汎用key loggerを作らない。

### 既存10基盤への接続

各デモ操作のbefore／afterはObserve、WaitStable、Compare、LearnTransitionへ渡す。デモ操作はAI proposalではないが、current window／frame／transformとdurable commitを満たしたTransition Evidenceとして保存する。AIでbuttonを探す条件、OCR類似照合、Moved裁定、操作拒否の所有境界は計画§0.3とFoundation Contractを変更しない。

## F／A／H

- F: Demonstration SessionとLearning Routeの責務境界、記録・再生排他、privacy境界、Phase Exit、commit／push。親が最終監査で裁定する。
- A: contract／store、Windows入力adapter、route導出、Host intents、Game Operator UI、製品journey test。Peertable席が工程正本からclaimする。
- H: 最後のNIKKE実UI 1:1操作デモ。人が実際に操作したUX証拠だけを所有し、機能中核の代替にしない。

## 非目標

- NIKKE以外の一般game対応claim。
- 戦闘、課金、現金購入を含むデモ実測。
- DLL注入、memory read／write、anti-cheat回避。
- 常時録画、desktop全体録画、他appのkey記録。
- G13／G600既存設定UI、fast path、device write方式の再設計。
- Demonstration Sessionを正解手順として無検証で再生する経路。
- OCR完全一致、destination一致、ReviewStatus、Verified環境を操作gateへ戻すこと。

## 既知の罠

- low-level hookのinjected flagだけではNanoと物理deviceを区別できない。記録と再生の排他で自己記録を構造的に防ぐ。
- mouse座標はdesktop絶対座標で保存せず、操作時current client frameへ正規化する。
- click直前だけcaptureしてbeforeを捏造しない。記録開始後のcurrent observationと操作時frameを束縛する。
- focus喪失中のkeyboard文字を保存しない。復帰時は新Observationから再開する。
- G13／G600 observerをfast path worker上でSQLite／WGCへ同期接続しない。
- ユーザーの寄り道やミスを原本から消さない。route導出結果と採否理由だけを別revisionへ置く。
- 既存InputStudioWindowのdevice配置やbinding editorをGame Operator都合で作り直さない。

## Lattice task仕様

以下は起票時の作業指定であり、状態・依存・完了証拠の正本はLattice storeだけである。

### t01-demonstration-contract

Demonstration Sessionの公開contract、validator、append-only store、migration、再openを実装する。goal、game profile、focus区間、有限操作、frame binding、before／after、Compare、evidence、原本revisionを保持する。Learning RouteやUIをこの工程で実装しない。

### t02-windows-input-recorder

Windows環境別mouse／keyboard recorderと、既存G13／G600 edge observerを実装する。対象game foregroundだけを記録し、focus喪失pause、client座標正規化、key down／upの有限化、記録・再生排他、fast path非blockingをfocused testとWindows native self-windowで確認する。

### t03-demonstration-route-compiler

Demonstration Sessionを既存Game Structure／Transition Evidence／Learning Routeへ導出する。Moved操作を候補edge列にし、Stayed／Undetermined、寄り道、重複の採否理由を残す。元sessionと既存routeを変更せず、新route revisionだけを作る。

### t04-recording-host-intents

記録開始／停止／状態／session一覧／記録からmacro作成をHost public intentsへ追加する。Phase 13のCodex monitored、AI free、game別workspace、Nano session、run evidenceへ接続し、同じ実行coordinator以外の経路を作らない。

### t05-game-operator-recording-ui

既存Game Operatorへgoal、記録開始／停止、focus pause、記録step一覧、記録からmacro作成、2 mode再生、進捗、停止理由を追加する。既存Input StudioのG13／G600画面を変更せず、内部IDやtokenを利用者へ露出しない。

### t06-macro-assignment-integration

デモ由来macroを既存合成、G13／G600割当、button queue、SQLite再openへ通す。同じmacro tokenとpublic intentsを使い、既存route、正常step、既存device設定を作り直さない。

### t07-product-journey-acceptance

fakeと実SQLite、Windows self-window、Nano非injected入力を使い、記録→route導出→AI監視修復→別process AI 0→統合→G13／G600割当→再openを同一public経路で一巡する。Computer Use、SendInput、外部AI APIは0。

### t08-owner-ui-live

NIKKEの可逆menu操作を利用者がGame Operatorの記録UIから一巡し、記録、候補route、AI監視修復、AI 0、割当表示を1:1確認する。課金、消費、募集、戦闘は対象外。これはH工程であり席はclaimしない。

### t09-phase14-exit

全A工程の関連test後にfull regressionを一回だけ実行する。Peertableで契約クリティカル範囲を独立反証し、4値Exit assessment、終端証拠、開発版再install、対象限定commit、origin/main pushを完了する。Hが未実施なら未確認と明記し、実施済みへ読み替えない。

## 依存の意図

t01完了後にt02とt03を並行可能とする。t04はt02／t03、t05はt04、t06はt04、t07はt05／t06、t08はt07、t09はt07／t08に依存する。同一repoの並行writerはLatticeが独立性を検証できない場合は直列へ落とす。大量の未追跡probe出力は削除しない。

## 検証順

characterization／focused test → module関連test → Windows native self-window → fake＋実SQLite product journey → NIKKE可逆live → Phase最終full regression一回 → 証拠 → 対象限定commit／push。
