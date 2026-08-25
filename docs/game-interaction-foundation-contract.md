# Game Interaction Foundation Contract

## 結論

Game Operatorの探索、構造学習、教師付きmacro、将来の自律実行は、`IGameInteractionRuntime`の基本10機能だけを通る。Probe固有スクリプト、ACK、raw frame SHA差分、既知labelを上位機能の成功根拠にしない。

保存済みpage／actionを常に先に使い、AIの`DiscoverTargets`を呼べるのは保存情報が無い時と、保存actionの送出後10秒で`Moved`を確認できなかった時だけである。OCR／AI文字、固定risk語、利用者確認、復帰edge、反復回数、destination IDは通常操作の受付条件を所有しない。

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
- `WindowsKnownFirstTargetDiscovery`: 保存page／actionを類似OCRで先に解決し、二つの許可条件でだけAI discoveryへ移る。
- `ProductGameExplorerRuntime`: 10の基盤機能、Durable Attempt、明示Game Policy、Structure学習、Explorer UI controlを一つのzero-seed stepへ合成する。送出前の追加AI再観測を行わない。
- `WindowsProductGameExplorerComposition`: WGC、Windows OCR、Foundry Local、Nano Serial HID、Game Policy、Structure Storeを接続するWindows正規入口。
- `PurposeDirectedExplorationRuntime`: 利用者goal、決定的Learning Route ID、route cursorを所有し、既存`ProductGameExplorerRuntime`だけを一手実行器として使う。`Moved` edgeを逐次appendし、`Stayed`／`Undetermined`は同じstepの学習継続、失敗stepの修復は当該edgeだけを新版で差し替える。
- `SemanticTextGoalCompletionEvaluator`: `Moved`したaction名またはafter sceneにあるlocal OCR／affordanceをgoal coreへ類似照合して初回完了を判定する。正規化後の空文字は候補にしない。操作受付gateではない。保存route再生は`Compiled` routeの全edge `Moved`で完了し、`Draft` prefixは再生後も探索を続ける。

目的runのCompareは、操作前と操作後の両方をcomparison-only local sceneで作る。AI／保存actionで一件に絞ったtarget sceneはdispatch、index、learningに保持し、異なるscene表現同士をCompareしない。state identityがAmbiguous／Insufficientでもactionable structureが同じなら`Stayed`、明確に変われば`Moved`、構造証拠が無い時だけ`Undetermined`とする。

保存routeのedgeにsemantic key、primitive、normalized boundsがあれば、current window／frame／transformへ直接再束縛してOCR state identityより先に実行する。正常`Moved`再生は同じStructure edgeを使い、新edgeを再commitしない。非遷移後だけ当該stepをAI repairへ移し、修復成功時だけ新版edgeへ差し替える。

Foundry Localのgoal指定responseはgoalとの類似／包含を満たす1件だけを受け、同一frame OCRへの束縛は完全一致でなく一意な類似候補を使う。page索引は1件以上の類似OCR anchorを保存でき、2件固定をdurable commit gateにしない。0件は既存visual evidence経路だけを使う。

## 禁止する成功判定

- Nano ACKまたはAPI戻り値だけで`Moved`にする。
- raw PNG SHA-256が変わっただけで`Moved`にする。
- timeoutを`Stayed`にする。
- timeoutや途中で古くなった安定候補を`Moved`にする。
- provider failureを別provider、OCR、既知fixtureへfallbackして成功扱いする。
- pointer移動後にOCR矩形を追跡し、別targetへ座標を補正する。
- OCRまたはAI labelの「購入」「戦闘」「開始」等を固定禁止tagへ変換して通常操作を拒否する。
- 一手承認、既知復帰edge、反復回数を満たさないことだけで通常操作を拒否する。
- 保存時より新しいStructure revisionで参照edgeが有効なのに、revision ID不一致だけでrouteを拒否する。
