# t04 安定待機とクリック遷移判定

## 結論

raw frame SHA、Nano ACK、API戻り値、固定sleepを意味遷移の根拠にせず、認識済みのstate候補とactionable structureだけで安定待機と`Moved`／`Stayed`／`Undetermined`を判定する機能を実装した。

## 意味構造

- state identityとstate candidate ID集合
- controlのsemantic kind
- controlのsemantic label
- control中心の画面内4×4配置帯

Observation ID、scene ID、frame sequence、PNG、animation pixelは同一性へ含めない。したがって同じcontrol構成を保った常時animationは安定可能である。

## 判定

- `Stayed`: stableな前後でstate候補とactionable structureが同じ。
- `Moved`: stableな前後でstate候補またはactionable structureが変わった。
- `Undetermined`: timeout、Unavailable、fault、capture binding変化、Ambiguous、InsufficientEvidence、意味証拠なし。

timeoutを`Stayed`へ変換しない。changed regionは前後のsemantic key差分に属するevidenceだけを返す。

## 実装

- `GameSceneSemanticComparer`
- `GameSceneStabilityWindow`
- `GameTransitionJudge`
- `GameInteractionStabilityRuntime`
- `AffordanceCandidate.SemanticKind／SemanticLabel`

## focused検証

- `GameTransitionJudgeTests`: 6件green
  - animation frameごとにObservation IDとboundsが変わっても`Stayed`
  - actionable structure変更は`Moved`
  - timeout／Unavailable／faultは`Undetermined`
  - Ambiguousは`Undetermined`
- `GameInteractionStabilityRuntimeTests`: 2件green
  - 連続意味構造でstable
  - timeoutをstable／Stayedへ丸めない
- 変更対象の`git diff --check`通過。

## 未検証

NIKKEの常時animationと実遷移を使ったWindows native判定は`t07-basic-live`で確認する。
