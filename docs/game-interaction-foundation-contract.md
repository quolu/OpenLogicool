# Game Interaction Foundation Contract

## 結論

Game Operatorの探索、構造学習、教師付きmacro、将来の自律実行は、`IGameInteractionRuntime`の基本10機能だけを通る。Probe固有スクリプト、ACK、raw frame SHA差分、既知labelを上位機能の成功根拠にしない。

## 10の基盤機能と現在地

| 基本機能 | 既存部品 | 製品runtime成立 |
|---|---|---|
| Observe | `ProductGameObservationRuntime`、`WindowsWgcGameFrameSource`、`ObservationResult` | Windows実画面で確認済み。WGCが静止中に新frameを出さない時は最後の有効frameを再観測し、明示capture faultでは再利用しない |
| DiscoverTargets | `FoundryLocalControlDiscoveryProvider`、`WindowsGameOcrRecognizer`、`FoundryLabelTargetDiscoveryAdapter` | 文字、icon-only、画像controlを確認済み。初回だけAIを使い、同一frameの局所画像へ束縛する |
| Hover | `NanoGameInteractionActions`、`SerialHidNanoGameInputDevice` | Nano送出確認済み。NIKKEの2対象は`Stayed`、GameLabのhover対応buttonは表示変化、索引保存、AIなし再実行まで確認済み |
| Click | `NanoGameInteractionActions`、`SerialHidNanoGameInputDevice` | NIKKE実画面で確認済み。操作前座標だけを使用 |
| KeyTap | `NanoGameInteractionActions`、`SerialHidNanoGameInputDevice` | NIKKEのEscで確認済み。有限down／up、索引保存、AIなし再実行まで成立 |
| Scroll | `NanoGameInteractionActions`、`SerialHidNanoGameInputDevice` | verticalをNIKKEランキングで確認済み。horizontalはNano 1.1.0非対応を明示 |
| Drag | `NanoGameInteractionActions`、`SerialHidNanoGameInputDevice` | NIKKEランキングでdown→move→upと内容移動を確認済み。fault時もupを送る |
| WaitStable | `GameSceneStabilityWindow`、`GameInteractionStabilityRuntime` | 意味構造のframe数と時間を満たした後も、操作後10秒間は遷移確認を継続する |
| Compare | `GameTransitionJudge` | 実装済み。`Moved`／`Stayed`／`Undetermined`、raw SHA不使用 |
| LearnTransition | `GameTransitionLearningController`、`ExplorationCoordinator.RecordOutcome`、Structure Event Store | dispatch receipt、判定、観測列をTransition Evidenceへ保存。静止WGCの同一frameによる`NoChange`も安定窓付きで保存する |

## 公開型

- `GameInteractionOperations`: 10の基盤機能の唯一の名前台帳。
- `GameInteractionTargetBinding`: Observation、frame sequence、transform revision、window、candidate、locatorへ入力対象を固定する。
- `GameInteractionDispatchReceipt`: Nano入力の送信結果。ゲーム内結果を含めない。
- `GameInteractionStabilityResult`: 意味構造の安定窓またはtimeout／failure。
- `GameTransitionComparison`: `Moved`／`Stayed`／`Undetermined`と差分根拠。
- `KnownScreenActionExecutionResult.TransitionObserved`: 保存済みaction送出後に`Moved`を確認したか。再探索を起動するかの判定はこの値だけを使う。
- `KnownScreenActionExecutionResult.DestinationMatched`: expected／observed state IDが厳密一致したか。OCR揺れで`TransitionObserved`を否定するためには使わない。
- `GameTransitionLearningRequest`: 操作前後、dispatch、安定窓、判定を既存Transition Evidenceへ渡す要求。
- `IGameInteractionRuntime`: 上位探索loopが使う唯一の製品port。
- `ProductGameExplorerRuntime`: 10の基盤機能、Durable Attempt、risk／承認、Structure学習、Explorer UI controlを一つのzero-seed stepへ合成する。
- `WindowsProductGameExplorerComposition`: WGC、Windows OCR、Foundry Local、Nano Serial HID、Game Policy、Structure Storeを接続するWindows正規入口。

## 禁止する成功判定

- Nano ACKまたはAPI戻り値だけで`Moved`にする。
- raw PNG SHA-256が変わっただけで`Moved`にする。
- timeoutを`Stayed`にする。
- timeoutや途中で古くなった安定候補を`Moved`にする。
- provider failureを別provider、OCR、既知fixtureへfallbackして成功扱いする。
- pointer移動後にOCR矩形を追跡し、別targetへ座標を補正する。
