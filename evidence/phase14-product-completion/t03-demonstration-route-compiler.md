# Phase 14 t03: DemonstrationからLearning Route導出

## 作ったもの

1. `src/OpenLogicool.Contracts/Playbooks/DemonstrationRouteCompilerContracts.cs`（新規）
   - `DemonstrationRouteDecisionKind`（Accepted／ExcludedStayed／ExcludedUndetermined／ExcludedDuplicate／ExcludedDetour）
   - `DemonstrationRouteDecision`（操作1件ごとの採否理由とcommit済みEdgeId）
   - `DemonstrationRouteCompilationResult`（導出結果：SessionId・作成したLearningRouteRevision・全Decision）
   - `DemonstrationGoalRouteIds.Create`（`OpenLogicool.Host.PurposeLearningRouteIds.Create`と同じ式。層の都合（HostがExplorationに依存し逆向きにできない）で複製したが、入力が同じなら出力も同じなので、demonstration由来のrouteはAI探索由来routeと同じgoal routeへ合流する）
2. `src/OpenLogicool.Exploration/DemonstrationRouteCompiler.cs`（新規）
   - `IDemonstrationRouteCompiler.Compile(DemonstrationSessionRecord)` / `DemonstrationRouteCompiler`実装
   - 停止済み原本の各Operation eventを判定する:
     - `Stayed`／`Undetermined`（またはMovedなのにStableSceneが無い異常値）→ commitせずreasonだけ残して除外
     - `Moved` → 操作自身のBefore／After／NormalizedPoint／KeyTokens／Scroll／Drag paramから`AffordanceCandidate`と`TransitionEvidence`をその場で構築し、既存の`IGameInteractionStructureCommitter`（`GameInteractionStructureLearner`）へcommit。これはGame Structureへの実際の書込みで、既存のAI探索と同じcommit経路を通る
     - commit結果のEdgeIdを使い、`GameSceneSemanticComparer.SignatureId`でbefore/after状態の意味signatureを比較。同じ(before, after, primitive)の組が既にrouteにあれば「重複」、afterのsignatureが既にこのsession内で経由済みの状態なら「寄り道」として、どちらもroute本体からは除外（Structureへのcommit自体は残る＝導出は成立している）
     - 採用された操作だけを順序どおりEdgeIdsへ積む
   - route id は `DemonstrationGoalRouteIds.Create(gameId, environmentScope, goal)` で決定的に算出し、`ILearningRouteStore.LoadLatest`で既存routeがあれば`ParentVersionId`をその`VersionId`にして新版を追記（既存routeのscope／goalが一致しない場合は例外で止める）。元session・既存route revisionはどちらも変更しない（読むだけ、appendは新版のみ）
   - 採用できる操作が1件も無い場合、記録停止前の原本を渡した場合、操作eventが無い場合はいずれも例外で止まる（黙って空routeを作らない）

## 最終試験内容と試験結果

Windows native（net10.0-windows／.NET SDK 10.0.400）。

focused（新規10件・全green、`tests/OpenLogicool.Exploration.Tests/DemonstrationRouteCompilerTests.cs`）
- `Moved_operations_become_route_edges_while_stayed_and_undetermined_are_excluded_without_committing`: Moved 1件→Accepted・commit 1回、Stayed／Undeterminedはcommitなしで除外理由だけ残る。
- `Returning_to_an_already_visited_state_is_excluded_from_the_route_as_a_detour`: A→B（採用）→B→A（寄り道として除外、EdgeIdはcommit済み・route本体には非採用）。
- `Repeating_the_same_transition_is_excluded_from_the_route_as_a_duplicate`: 同じ(A→B, click)を2回→2件目は重複として除外。
- `First_compilation_for_a_goal_creates_a_root_revision_with_the_deterministic_goal_route_id`: 既存routeが無いとき、RouteIdが`DemonstrationGoalRouteIds.Create`と一致・ParentVersionId=null・RevisionNumber=1・Author=User・Status=Compiled。
- `Compilation_appends_onto_an_existing_route_for_the_same_goal_instead_of_replacing_it`: 既存route revision 1件をfakeへ用意→導出後はParentVersionId=既存VersionId・RevisionNumber=2・RouteId不変（新route作成ではなく追記）。
- `Compilation_refuses_when_the_existing_route_scope_or_goal_does_not_match_the_session`: 既存route（別environment）がある状態で導出すると例外。
- `Compilation_refuses_a_session_that_is_still_recording`: State=Recordingの原本を渡すと例外。
- `Compilation_refuses_a_session_with_no_operation_events`: Operation eventが無い（Stoppedのみ）と例外。
- `Compilation_refuses_when_every_operation_is_excluded`: Stayedしか無いsessionは採用0件で例外。
- `Key_tap_operations_are_committed_with_their_key_tokens_and_no_pointer_bounds`: key-tap操作はcommit時にKeyTokensがcandidateへそのまま渡り、bounds=[0,0,0,0]・LocatorType="demonstration-key"。

→ 成功! 失敗:0、合格:10、合計:10

関連test（module単位・最終確認）
- `dotnet test tests/OpenLogicool.Exploration.Tests` → 成功! 失敗:0、合格:63、合計:63（既存53件＋新規10件、既存の回帰なし）
- `dotnet test tests/OpenLogicool.Architecture.Tests` → 成功! 失敗:0、合格:8、合計:8（層違反なし。DemonstrationRouteCompilerはExplorationプロジェクト内に置き、Playbooks→Exploration方向の新規参照は作っていない）
- `dotnet test tests/OpenLogicool.Conformance.Tests` → 成功! 失敗:0、合格:61、合計:61

full regressionは実行していない（t02が同時にWindowsDemonstrationInputCollector.cs実装中で、`dotnet build`時点でそちらのfileにビルドエラーが残っている。t03が触った3fileだけを対象にビルド・testしており、t03の変更に起因するエラーではないことは上記の個別プロジェクトbuild／testで確認済み）。

## 自己監査で確認した点

- `LearningRouteContracts.cs`・`DemonstrationSessionContracts.cs`・`GameInteractionContracts.cs`・`DeviceContracts.cs`は読むだけで変更していない（koharuのt02作業と書込み境界が重ならないことをroomで事前合意済み、room seq 1027/1028）。
- Playbooksプロジェクトは変更していない（当初Playbooksへ置く想定だったが、`OpenLogicool.Exploration`が`OpenLogicool.Playbooks`に依存する向きのため、Structure commitへ直接アクセスする必要があるcompiler本体はExploration側に置いた。Contracts側の新規fileだけで完結する型はPlaybooks名前空間のままContracts配下へ置いている）。
- `dotnet build src/OpenLogicool.Exploration/OpenLogicool.Exploration.csproj`・`dotnet build`（新規3file関連プロジェクトのみ）は警告0・エラー0。

## 4値

Moved操作をStructureへcommitしcandidate edgeにする＝確認済み（focused test＋実際に`IGameInteractionStructureCommitter`の実装契約どおり呼び出し、Exploration.Tests既存63件は回帰なし）／Stayed・Undetermined・寄り道・重複の採否理由を残す＝確認済み（focused test 4種）／元sessionと既存routeを変更せず新route revisionだけを作る＝確認済み（読み取り専用の`Load`系だけ使用し、書込みは`ILearningRouteStore.Append`の新版だけ）／AI探索由来routeとの合流（同じgoal routeへの追記）＝確認済み（focused test）／実機での往復再生（demonstration→route→ProductGameExplorerRuntimeでの実replay）＝未確認（t03のscope外。次工程でHost層の配線とUI・実機接続が必要）。
