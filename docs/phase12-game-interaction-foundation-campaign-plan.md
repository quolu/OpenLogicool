# Phase 12 Game Interaction Foundation

## 結論

教師付きVisual Macroの実ゲーム再生より先に、Game Operatorが画面を観測し、候補を見つけ、有限入力を一回だけ送り、画面状態の変化を判定し、遷移として保存できる共通基盤を成立させる。

Phase 9で作成した`ObservedScene`、`AffordanceCandidate`、`ExplorationCoordinator`、Durable Attempt、Structure Event Storeは再利用する。一方、Probe固有スクリプトを製品runtimeの成立根拠にせず、Capture／AI／Exploration／Host／Nano adapterの責務を分離した公開portへ接続する。

工程の正本はLattice plan `phase12-game-interaction-foundation`とする。本書は目的、判断、受入条件、非目標だけを所有する。

## 着手理由

2026-08-25のNIKKE実走で、Phase 9／12の既存実装には次の欠落があると確認した。

- `IHostExplorerRuntimeControl`の製品実装がなく、実探索loopがHostへ配線されていない。
- 実ゲーム操作は必須labelとpinned observationを受け取るProbe固有スクリプトであり、zero-seed探索の共通入口ではない。
- クリック後の判定がraw PNG SHA-256の変化を成功根拠に含み、常時アニメーションと意味のある画面遷移を区別できない。
- Hover、Click、KeyTap、Scroll、Dragを同じObservation／Nano／evidence契約で扱う製品portがない。
- Phase 9 Exitの「探索基盤成立」は、contract／fake／個別probeの成立と製品runtimeの成立を混同している。

## 基本機能

次の10機能をすべて実装対象とする。上位の判定、探索、構造学習、macro再生はこの基盤を迂回しない。

1. `Observe`: 対象windowからfreshな画面と座標変換を取得する。
2. `DiscoverTargets`: game固有state名、target名、座標、正解routeを与えず、押せそうな文字・アイコン・画像領域を列挙する。
3. `Hover`: 観測済み候補へNanoでpointerを一度だけ移動し、入力前後を観測する。
4. `Click`: 操作前Observationに固定した座標をNanoで一度だけdown／upする。
5. `KeyTap`: 単一keyまたは有限chordをNanoでdown／upし、押下を残さない。
6. `Scroll`: 指定方向・指定量のwheel入力をNanoで一度だけ送る。
7. `Drag`: 観測済み始点から終点へNanoでdown→move→upを一度だけ行う。
8. `WaitStable`: raw pixel一致ではなく、主要領域、文字、affordance集合、state候補の構造が連続して同じになるまで待つ。
9. `Compare`: 操作前後を`Moved`／`Stayed`／`Undetermined`へ分類し、差分根拠を返す。
10. `LearnTransition`: Before Observation、操作、After Observation、判定、入力receiptを一つのTransition Evidenceとしてappend-only保存する。

## クリック遷移判定

最初の判定機能は次の一本道とする。

`Observe → DiscoverTargets → Click → WaitStable → Observe → Compare → LearnTransition`

- `Moved`: 意味のある画面状態、modal、tab、選択状態、actionable structureの変化を確認した。
- `Stayed`: 操作後も操作前と同じ意味状態で安定した。
- `Undetermined`: timeout、capture不能、認識不足、遷移途中、複数候補などで確定できない。
- 入力未送信は判定に丸めず`DispatchFailed`として別軸にする。
- timeoutを`Stayed`にしない。
- raw frame SHA差分、ACK、API戻り値、固定sleep、AI自己申告だけを`Moved`の根拠にしない。

## 共通不変条件

- 入力は操作直前のObservation、frame sequence、transform revision、target window、locator revisionへ束縛する。
- buttonの実座標は操作前に確定し、pointer移動後のOCR枠を追跡して補正しない。
- 入力routeはNano Serial HIDだけとし、SendInput／Computer Use／別routeへfallbackしない。
- 一操作につきdispatchは一回だけとし、自動retryを持たない。
- 入力送信結果とゲーム内結果を別軸で記録する。
- OS固有capture／window／cursor処理、Nano transport、Foundry／OCR、Probe evidence harnessを別ファイル・別adapterに隔離する。
- Probeは製品portを呼ぶだけとし、探索判断、安定判定、遷移判定をProbe内へ実装しない。

## 受入条件

1. 基本10機能に公開contract、製品実装、focused test、明示failureがある。
2. fake frame列で、常時アニメーション中でも同一画面を`Stayed`、実遷移を`Moved`、timeout／曖昧を`Undetermined`と判定する。
3. `IHostExplorerRuntimeControl`の製品実装が、WGC→認識→Nano→安定待機→判定→Structure Event Storeを一巡する。
4. 空DBとgame固有seed 0から候補を列挙し、一候補の操作結果をScreen Graphへ保存する。
5. NIKKE実画面で基本10機能を個別に実証する。gameが受理しないprimitiveは入力receiptと画面結果を分け、Unsupportedを成功へ丸めない。
6. NIKKEで複数の安全な候補を探索し、state、target、edge、no-changeを人のstate／target命名なしに保存する。
7. SendInput 0、Computer Use dispatch 0、fallback 0、blind retry 0、課金／希少資源消費／account変更0を証拠化する。
8. Phase 9／12の成立主張を実装と再照合し、過大な記述を訂正する。
9. focused test後に関連test、最終full regression、独立反証、対象限定commit／pushまで閉じる。

## 非目標

- NIKKE全日課の完遂
- 学習済みfixtureをzero-seed成立証拠へ使うこと
- raw pixel差分だけによる画像分類
- cloud AI、外部AI API、外部AI API費用
- Computer Useによるゲーム操作
- 購入、課金、希少資源消費、account変更、自由text入力の自動探索
- Windows以外のruntime対応

## 工程参照

実行ToDo、依存、状態、完了証拠はLattice plan `phase12-game-interaction-foundation`だけを正本とする。
