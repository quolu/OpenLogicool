# Phase 12 Supervised Visual Macro Runner Exit判定

判定日: 2026-08-25

## 結論

Exit成立。保存済みLearning RouteからpinしたVisual Macroを、10の基盤機能、append-only journal、Nano Serial HID、製品UI portへ接続し、NIKKEの非課金・非消費・可逆2stepを実ゲームで完了した。

公開可能な主張は「保存済みの確認済み画面構造とactionを使う教師付きVisual Macro」である。NIKKE全日課の無人完遂、未知画面の自動修復、zero-seed複数target探索は含まない。

## Exit判定

| 条件 | 4値 | 根拠 |
|---|---|---|
| Learning Route版とStructure版をrun開始時にpinする | 確認済み | SupervisedVisualMacroRunner、Host／Persistence focused test |
| Beforeで保存ページ／actionを観測できない時は入力0 | 確認済み | OCR欠測は最大10秒待つ。page/action不成立負例とNIKKE Observe Only dispatch 0 |
| journal dispatch commit後にNanoを一回だけ送る | 確認済み | final journalのdispatch／dispatch-result各2、retry 0 |
| Afterは10秒のMovedだけで次stepへ進む | 確認済み | t06の2 transitionがStable＋Moved。destination IDは診断のみ |
| 現在step、監査、送信、停止理由、履歴をUIへ表示する | 確認済み | LearningRoutePanel、SupervisedMacroRunPresenter、Desktop test |
| NIKKE可逆sliceを実ゲームで一巡する | 確認済み | [t06](../evidence/phase12-supervised-macro-runner/t06-nikke-live-slice.md) |
| 禁止riskをcompile／runで拒否する | 確認済み | compiler／Host focused test |
| SendInput／Computer Use／fallback／retry 0 | 確認済み | t06 machine summary |

## 過剰な操作拒否の撤去

教師付きruntimeは独自の認識・入力・遷移判定を持たず、LearnedSceneMatcher、NanoGameInteractionActions、GameInteractionStabilityRuntime、GameTransitionJudgeを利用する。

操作前OCRは完全一致を要求せず、Knownな保存ページとactionが一度得られるまで最大10秒観測する。操作後は10秒間の意味的遷移を判定し、Movedならdestination IDが診断上不一致でも次へ進む。

## 検証

- 変更直結関連test 676件green。
- solution full regression 1208件green、失敗0。
- 独立反証: window bounds／transform、WGC cancellation／typed faultを含めてP1／P2残件なし。
