# t05 遷移証拠の永続化

## 結論

Nano送信結果、before／after Observation列、意味判定、差分根拠を既存Durable AttemptのTransition Evidenceへ接続した。DispatchFailedは画面遷移へ変換せず、遷移未学習として分離する。

## outcome変換

- `Moved`＋after `Novel` → `Novel`
- `Moved`＋after `Known` → `Destination`
- `Stayed` → `NoChange`
- `Undetermined` → `OutcomeUnknown`
- `DispatchFailed` → Structure outcomeを作らない

## 永続化内容

- Before／After Observation ID
- Attempt／candidate／primitive
- Nano `GameInteractionDispatchReceipt`
- `GameTransitionComparison`
- 安定待機中のObservation ID列
- dispatch／observation完了monotonic時刻
- environment／Exploration Run

`ExplorationCoordinator.RecordOutcome`がTransition Evidence JSONをStructure Event Storeへappendする。Projectionは既存のDispatchArmed→OutcomeRecorded順序を維持する。

## focused検証

- `GameTransitionLearningControllerTests`: 6件green
  - 4種類のoutcome変換
  - DispatchFailed時recorder call 0
  - after Observation不一致をstore call前拒否
- `ExplorationCoordinatorTests`のDurable Attempt scenario: 1件green
  - Nano receipt、Moved判定、Observation列が返却evidenceとStructure Event payloadの両方に残る
- `OpenLogicool.Exploration.Tests`全28件green、failure 0、skip 0。
- 変更対象の`git diff --check`通過。

## 未検証

実SQLite再openとNIKKE live evidenceは製品Explorer runtime統合後に確認する。
