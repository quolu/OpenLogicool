# Game Interaction Foundation Contract

## 結論

Game Operatorの探索、構造学習、教師付きmacro、将来の自律実行は、`IGameInteractionRuntime`の基本10機能だけを通る。Probe固有スクリプト、ACK、raw frame SHA差分、既知labelを上位機能の成功根拠にしない。

## 基本機能と現在地

| 基本機能 | 既存部品 | 製品runtime成立 |
|---|---|---|
| Observe | `ProductGameObservationRuntime`、`WindowsWgcGameFrameSource`、`ObservationResult` | 実装済み。Windows実画面は後続gate |
| DiscoverTargets | `FoundryLocalDiscoveryVisionProvider`、`WindowsGameOcrRecognizer`、`FoundryLabelTargetDiscoveryAdapter` | 文字付きcontrolを実装済み。icon-onlyは現modelで非対応 |
| Hover | `NanoGameInteractionActions`、`SerialHidNanoGameInputDevice` | 実装済み。Windows実画面は後続gate |
| Click | `NanoGameInteractionActions`、`SerialHidNanoGameInputDevice` | 実装済み。操作前座標だけを使用。Windows実画面は後続gate |
| KeyTap | `NanoGameInteractionActions`、`SerialHidNanoGameInputDevice` | 実装済み。有限down／up。Windows実画面は後続gate |
| Scroll | `NanoGameInteractionActions`、`SerialHidNanoGameInputDevice` | vertical実装済み。horizontalはNano 1.1.0非対応を明示 |
| Drag | `NanoGameInteractionActions`、`SerialHidNanoGameInputDevice` | 実装済み。down→move→up、fault時もupを送る。Windows実画面は後続gate |
| WaitStable | `GameSceneStabilityWindow`、`GameInteractionStabilityRuntime` | 実装済み。意味構造のframe数と時間を両方要求 |
| Compare | `GameTransitionJudge` | 実装済み。`Moved`／`Stayed`／`Undetermined`、raw SHA不使用 |
| LearnTransition | `GameTransitionLearningController`、`ExplorationCoordinator.RecordOutcome`、Structure Event Store | 実装済み。dispatch receipt、判定、観測列をTransition Evidenceへ保存 |

## 公開型

- `GameInteractionOperations`: 基本10機能の唯一の名前台帳。
- `GameInteractionTargetBinding`: Observation、frame sequence、transform revision、window、candidate、locatorへ入力対象を固定する。
- `GameInteractionDispatchReceipt`: Nano入力の送信結果。ゲーム内結果を含めない。
- `GameInteractionStabilityResult`: 意味構造の安定窓またはtimeout／failure。
- `GameTransitionComparison`: `Moved`／`Stayed`／`Undetermined`と差分根拠。
- `GameTransitionLearningRequest`: 操作前後、dispatch、安定窓、判定を既存Transition Evidenceへ渡す要求。
- `IGameInteractionRuntime`: 上位探索loopが使う唯一の製品port。
- `ProductGameExplorerRuntime`: 基本10機能、Durable Attempt、risk／承認、Structure学習、Explorer UI controlを一つのzero-seed stepへ合成する。
- `WindowsProductGameExplorerComposition`: WGC、Windows OCR、Foundry Local、Nano Serial HID、Game Policy、Structure Storeを接続するWindows正規入口。

## 禁止する成功判定

- Nano ACKまたはAPI戻り値だけで`Moved`にする。
- raw PNG SHA-256が変わっただけで`Moved`にする。
- timeoutを`Stayed`にする。
- provider failureを別provider、OCR、既知fixtureへfallbackして成功扱いする。
- pointer移動後にOCR矩形を追跡し、別targetへ座標を補正する。
