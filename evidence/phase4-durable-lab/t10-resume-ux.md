# t10-resume-ux 証跡

- 実装者: なぎ（pull run `phase4-durable-lab-20260819-122051`・base `6a873eb`）
- 対象: PB-009（再開前の対象 app・version・現在 Observation 照合。UniqueMatch 以外は自動再開しない）・UX-005（再開時に最後の confirmed state・現在 state・差分・採用 version・次の操作を表示）・§6.8（manual intervention 終了後は必ず新 Observation から照合）。実画面 UniqueMatch は対象外（Phase 5）——本実装は pure で Observation の出所を知らず、検証は fake Observation 相当の合成データだけで行った。

## 何を作ったか（追加5ファイルのみ・Contracts／Persistence／既存ファイル無変更）

1. **`src/OpenLogicool.Domain/ResumeGate.cs`**（pure）
   - `StateMatchResult`: §6.8 の閉集合5値（UniqueMatch／NoMatch／AmbiguousMatch／InsufficientEvidence／StaleObservation）。
   - `StateMatcher.Match(observation, expectedStateId, freshnessBudgetMs, stabilityWindowMs)`: ObservationStatus→5値の写像。**判断した写像**: Unavailable／Unknown→InsufficientEvidence（認識不能は「期待と違うと分かった」ではない）、Ambiguous→AmbiguousMatch、Known は鮮度予算超過→StaleObservation・安定窓（frame `LastChangeMs`）未達→InsufficientEvidence・唯一候補一致→UniqueMatch・不一致→NoMatch。Known で候補が1件でない Observation は契約違反として例外（黙って丸めない）。予算・安定窓は正の値を呼び出し側が明示（無根拠 default を置かない）。
   - `ResumeGate.Judge(ResumeCheckInputs)`: PB-009 の照合（app identity・target window＝frame source・Playbook version・state match・run 閉止・再観察充足）。**AutoResume は全条件成立の時だけ**。拒否理由は一つで止めず**全列挙**（`ResumeBlockReason`）。app identity 取得不能（null）は不一致側＝自動再開しない。
2. **`src/OpenLogicool.Playbooks/ResumeReadiness.cs`**（pure・journal event 直読）
   - t05 interface 決定（room [90]・[97]）の3 payload type を前提: `IsRunClosed`（`abandon` あり＝閉じた run・再開不可）、`AdoptedVersionId`（最後の `version-switch` event の PlaybookVersionId、無ければ先頭 event の pin）、`SatisfiesReobservation`（最後の `manual-intervention` event より後に、再開照合へ使う ObservationId の observation event が commit されている時だけ真。介入開始だけで crash した run は偽＝安全側）。
   - **RunProjection には依存しない**——3 type の projection 統合（t09 の縫い目）は bell 仕分けの別件のまま（room [92] で宣言済み）。wire 文字列の定数正本は t05 着地後の `RunEventPayloadTypes`。
3. **`src/OpenLogicool.Playbooks/ResumeReport.cs`**（pure builder）
   - UX-005 の5項目を値として組む `ResumeReportView`: 最後の confirmed state（最後の confirmation event の ObservationId を呼び出し側供給の observationId→stateId 対応で解決。解決できなければ null のまま・**補完しない**）・現在 state（Known 唯一候補だけ）・差分（Same／Different／Unknown——どちらか不明なら Unknown）・採用 version（`ResumeReadiness.AdoptedVersionId`。渡された graph の version と不一致なら例外）・次の操作（採用 version の graph で現在 state と**唯一**対応する node の SemanticActionId。無い・複数・action なしは null＝提示しない）。

## どう確認したか（focused test・worktree 内で実行）

- **`tests/OpenLogicool.Domain.Tests/ResumeGateTests.cs`（16件追加）**: 写像の全分岐（認識不能2種・Ambiguous・一致/不一致・鮮度超過・安定窓未達・Known 非唯一候補の拒否）／全条件成立時だけ許可／UniqueMatch 以外4値の全拒否／app 不明・不一致／**6条件同時不成立の全列挙**／閉じた run・version 不一致／再観察未充足。
- **`tests/OpenLogicool.Playbooks.Tests/ResumeTests.cs`（9件追加）**: abandon の閉止判定／採用 version（pin→switch 移動・空列拒否）／再観察（介入なし＝真・**介入後の新 Observation だけ真・介入前の Observation は偽**・介入後に観測が無い run は偽）／UX-005 5項目の突合／不明時は Unknown（補完なし）／対応 node が無い state は次操作 null／switch 後 version の graph 検証。
- 実行結果（worktree・build 出力は一時 `Directory.Build.props` で scratchpad へ redirect・accept 前に削除）:
  - `dotnet test tests/OpenLogicool.Domain.Tests` → **71件 green**（既存55＋追加16）
  - `dotnet test tests/OpenLogicool.Playbooks.Tests` → **59件 green**（既存50＋追加9）
  - `dotnet test tests/OpenLogicool.Architecture.Tests` → **4件 green**（redirect 非両立のため in-tree 実行→obj/bin 即削除。csproj・project 参照は無変更）
- 再走コマンド: worktree 直下に redirect の一時 `Directory.Build.props` を置き `dotnet test tests/OpenLogicool.Domain.Tests`／`tests/OpenLogicool.Playbooks.Tests`（手法は room [15] と同じ）。

## 実装しなかったもの（判断）

- **RunProjection／SessionReplayer の3 type 統合**: t09 成果の拡張であり bell 仕分けの別件（room [89][92]）。t10 は event 直読で着地順に依存しない。
- **GameLab 画面への再開表示の配線**: `ResumeReportView` は表示材料の pure record まで。t08 の GameLab 画面への統合は t05/t10 着地後の統合面（t11 Exit か maintenance）。
- **実画面 UniqueMatch・観測の自動再取得**: Phase 5 の面。本実装は照合と判定だけを所有する。
- t05 の RunControls との配線（再開実行そのもの・Paused 状態遷移）: t05 所有。ResumeGate は「自動再開してよいか」の判定を返すだけで、実行の口を持たない。
