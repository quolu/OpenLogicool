# Phase 14 t04: Recording Host public intents／Phase 13接続

## 作ったもの

1. `src/OpenLogicool.Contracts/Playbooks/DemonstrationRecordingIntentsContracts.cs`（新規）
   - `DemonstrationSessionSummary`／`DemonstrationRecordingStatus`／`DemonstrationLiveSession`（live wiring境界）／`IDemonstrationLiveSessionFactory`（対象processを実windowへ解決しlive sessionを作る環境別境界）／`IDemonstrationRecordingIntents`（Start／Stop／Status／ListSessions／CreateMacroFromSession）。
2. `src/OpenLogicool.Host/DemonstrationRecordingPump.cs`（新規）— OS hookが同期・非blockingで呼ぶ`IDemonstrationInputSink.Observe`と、`DemonstrationRecorder.HandleAsync`（WaitStable等で待つ非同期処理）を橋渡しするChannelベースのpump。既存コードには この橋渡しが無く（t02のprobeは`CollectingSink`で観測だけ確認していた）、記録器を実際に駆動する経路が欠けていたため作成した。
3. `src/OpenLogicool.Host/HostDemonstrationRecordingIntents.cs`（新規）— `IDemonstrationRecordingIntents`実装。
   - 対象game選択はPhase13の`HostMacroAutomationIntents`と同じ`MacroTargetSettingsStore`（同じdatabase file）を読むだけで、別の選択状態を作らない。
   - `StartAsync`: 選択済み対象を`IDemonstrationLiveSessionFactory`へ渡しlive sessionを取得→`DemonstrationRecorder`を組み立て開始→pumpでcollectorと接続。
   - `StopAsync`: collector停止→pump排出（受理済みedgeを処理し終えるまで待つ）→記録停止event追記→live資源解放。
   - `Status`／`ListSessions`（`demonstration_sessions`のgame_id×environment_scope distinct列挙経由）。
   - `CreateMacroFromSession`: 停止済み原本をLoadし、`SqliteGameStructureStore`／`SqliteLearningRouteStore`／`StructureKnowledgeController`／`ExplorationCoordinator`／`GameInteractionStructureLearner`を同一connectionで組み立て、t03の`DemonstrationRouteCompiler`へ渡してLearning Route新版を作り、`MacroCatalogItem`として返す（既存`HostMacroAutomationIntents.ListMacros`と同じ形＝そのまま既存再生経路で再生できる）。
4. `src/OpenLogicool.Host/HostMacroAutomationIntents.cs`（既存改変）— コンストラクタへ`DemonstrationRecordingGate? recordingGate = null`を追加（無指定時は専用instanceで従来どおり）。`ExecuteAsync`が`gate.TryBeginPlayback`を通ってから実行し、`finally`で`EndPlayback`する。録画中intentsと同じgate instanceを渡せば、記録中の再生・記録中への記録開始が構造で排他される。既存呼び出し元（Program.cs・2 test file）は無指定のままで動作不変。

## 発見して直した先行工程由来の欠陥（憲章16）

t04着手時、t03の`DemonstrationRouteCompiler`が実装した`Structure` commitが、実際の`GameInteractionStructureLearner`／`StructureKnowledgeController`では拒否される欠陥を発見した（t03のfocused testはfake委譲だけで実装を通していなかったため未検出）。原因は、StructureKnowledgeControllerがdelta operationの参照evidenceを事前記録済み前提とし、かつGameStructureProjector.ReplayがOutcomeRecordedの前に同じAttempt/CorrelationIdのDispatchArmedを要求すること。AI探索はExplorationCoordinatorのdispatch確率機構がこれを書くが、demonstrationはその機構を通らない。`DemonstrationRouteCompiler`へ`IGameStructureStore`を追加し、Actor=UserのDispatchArmed→OutcomeRecordedを直接登録してから既存commit経路へ渡すよう修正し、実コンポーネントを通す統合testを追加した（commit 8917583、t03のreopenはしていない＝本工程の修正として実施）。

## 最終試験内容と試験結果

Windows native（net10.0-windows／.NET SDK 10.0.400）。

focused（新規、全green）
- `tests/OpenLogicool.Host.Tests/HostDemonstrationRecordingIntentsTests.cs` 7件: 未選択targetでStart拒否／Start後Status=Recording／二重Start拒否／未Start Stop拒否／実際にPointerDown→PointerUpをpump経由で処理しStop後にOperationCount=1・ListSessionsへ反映／記録中は共有gateがmacro再生をTryBeginPlayback=false（拒否理由あり）で拒み停止後は再生可能に戻る／停止済み原本からCreateMacroFromSessionが実GameInteractionStructureLearner等を組み立ててStepCount=1のMacroCatalogItemを作る。
  → 成功! 失敗:0、合格:7、合計:7
- `tests/OpenLogicool.Host.Tests/HostMacroAutomationIntentsTests.cs` 既存7件＋新規1件（`Shared_recording_gate_refuses_playback_while_a_demonstration_is_recording`: 録画中はCreateAsyncが例外、EndRecording後は成功）。
  → 成功! 失敗:0、合格:8、合計:8

関連test（module単位・最終確認）
- `dotnet test tests/OpenLogicool.Host.Tests` → 成功! 失敗:0、合格:282、合計:282（既存回帰なし）
- `dotnet test tests/OpenLogicool.Architecture.Tests` → 成功! 失敗:0、合格:8、合計:8（層違反なし）
- `dotnet test tests/OpenLogicool.Exploration.Tests` → 成功! 失敗:0、合格:64、合計:64

full regressionは実行していない（t05以降が同じdirty treeで進行中の可能性あり。個別プロジェクトのbuild／testはすべてwarning 0・error 0・green）。

## このtaskで作らなかったもの（範囲外・正直な残課題）

- 実Windows環境（WGC capture＋perception認識）を使う`IDemonstrationLiveSessionFactory`の具体実装は作っていない。既存の`WindowsProductGameExplorerComposition`はNano送信を必須引数に持ち、記録専用（送信なし）の合成には使えないため、新規の「observation-onlyのWindows合成」が必要になるが、これは実機・実capture前提の別工程であり、bell #1049（NIKKE実機へ触れない方針）とも整合するようt04では立てていない。`IDemonstrationLiveSessionFactory`という差し込み点だけを用意した。
- Program.cs／Desktop UIへの配線（CLIコマンドやGame Operator画面への公開）はしていない。IntentsのAPI契約と実装ロジックのみが本工程の範囲。

## 4値

記録開始／停止／状態／session一覧をHost intentsへ追加＝確認済み（focused test）／記録から作るmacroが既存再生経路（MacroCatalogItem）へ合流する＝確認済み（focused test、実Structure/Route store使用）／記録と再生の排他が同じgateで構造的に成立＝確認済み（focused test、双方向）／別の実行coordinatorを作っていない＝確認済み（同一MacroTargetSettingsStore・同一DemonstrationRecordingGate・既存MacroCatalogItem形状の再利用）／実Windows環境でのlive記録（実device・実capture）＝未確認（次工程・実機手番）。
