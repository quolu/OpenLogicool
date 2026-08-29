# Phase 14 t05: Game Operator recording UI

## 作ったもの

1. `src/OpenLogicool.Contracts/Playbooks/DemonstrationRecordingIntentsContracts.cs`（既存拡張）
   - `DemonstrationSessionSummary`／`DemonstrationStepSummary`へ`DisplayLabel`（内部idを出さず目的・操作数・状態・日時／段番号・操作・遷移だけを表示）を追加。
   - `IDemonstrationRecordingIntents`へ`ListSteps(string sessionId)`を追加。
2. `src/OpenLogicool.Host/HostDemonstrationRecordingIntents.cs`（既存拡張）— `ListSteps`実装。指定sessionの操作eventを番号付きで、primitiveと遷移判定を日本語ラベルへ変換して返す（`OperationId`／`EdgeId`等は返さない）。
3. `src/OpenLogicool.Desktop/DemonstrationRecordingWorkspace.cs`（新規）— UI非依存のHost intents薄い委譲層（`MacroAutomationWorkspace`と同じ形）。
4. `src/OpenLogicool.Desktop/DemonstrationRecordingPanel.cs`（新規）— Game Operatorの「記録」tab本体。目的入力・記録開始／終了・記録中状態（一時停止表示含む、500ms polling）・記録済みデモ一覧・選択デモの操作一覧・「このデモからマクロを作る」を持つ。2 mode再生・進捗・停止理由は作り直さず、マクロ作成後は既存の「マクロ」tabへ切替えて選択させるだけ（`MacroAutomationPanel`を再利用）。
5. `src/OpenLogicool.Desktop/GameOperatorWindow.cs`（既存拡張）— `demonstrationRecordingIntents`引数を追加し、指定時だけ「記録」tabを「STEP 0　Web調査」と「マクロ」の間に追加。既存tab（Input StudioのG13／G600画面は別window で本タスクは触っていない）は変更していない。
6. `src/OpenLogicool.Desktop/MacroAutomationPanel.cs`（既存拡張）— 外部（記録tab）が新しいmacroを作った後にこのtabの一覧を反映させる`RefreshFromExternalCreation(routeId)`を追加しただけ。既存の内部`Refresh`をpublicから呼べるようにした以外の変更なし。

## 最終試験内容と試験結果

Windows native（net10.0-windows／.NET SDK 10.0.400）。

focused（新規、全green）
- `tests/OpenLogicool.Desktop.Tests/DemonstrationRecordingWorkspaceTests.cs` 3件: 目的の前後空白除去して委譲／空白目的はintentsを呼ばずに例外／一覧・step・macro作成の値がそのまま透過する。
- `tests/OpenLogicool.Desktop.Tests/GameOperatorMacroUiTests.cs` 既存2件＋新規2件: `demonstrationRecordingIntents`指定時は「STEP 0　Web調査」「記録」「マクロ」の順でtabが並び「記録」tabの中身がUserControl／未指定時は「記録」tabが無い。
  → 成功! 失敗:0、合格:7、合計:7
- `tests/OpenLogicool.Host.Tests/HostDemonstrationRecordingIntentsTests.cs` に`ListSteps`のfocused test1件追加（記録した1操作が「クリック」「画面が変わった」として返る、内部idは比較していない＝露出していないことの確認）。
  → 成功! 失敗:0、合格:7、合計:7

関連test（module単位・最終確認）
- `dotnet test tests/OpenLogicool.Desktop.Tests` → 成功! 失敗:0、合格:103、合計:103（既存回帰なし）
- `dotnet test tests/OpenLogicool.Architecture.Tests` → 成功! 失敗:0、合格:8、合計:8（層違反なし）
- `dotnet test tests/OpenLogicool.Host.Tests` → 失敗:1、合格:285、合計:286。**失敗はt06（koharu、同時進行中）が追加した未commit file `tests/OpenLogicool.Host.Tests/DemonstrationMacroAssignmentScenarioTests.cs`（`git status`で確認、私は触っていない）由来で、t05が触った9 fileはすべて対象限定buildとtestで確認済み。t05起因の欠陥ではない。**

full regressionは実行していない（t06が同時進行中のため）。

## 4値

目的入力・記録開始／終了をGame Operatorへ追加＝確認済み／focus一時停止の表示＝確認済み（Status().StatusのPaused分岐）／記録step一覧（内部id非露出）＝確認済み／記録からmacro作成＝確認済み／2 mode再生・進捗・停止理由（既存マクロtab再利用、作り直していない）＝確認済み（tab切替とRefreshFromExternalCreationの配線）／既存Input Studio画面の非変更＝確認済み（本タスクはGameOperatorWindow／MacroAutomationPanelだけを触り、Input Studio window fileは変更していない）／実機でのUI目視・実際の記録操作＝未確認（次工程・実機手番。IDemonstrationLiveSessionFactoryの実Windows実装はt04時点で範囲外と明記済み）。
