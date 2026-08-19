# t06-fake-observation 証跡 — fake Observation 4状態と Confirmed 契約

- 実施: 2026-08-19（hotaru・pull run `phase4-durable-lab-20260819-122051`・worktree base ce4cb20）
- 仕様正本: docs/phase4-campaign-plan.md §t06、docs/contracts/observation-result.md、docs/development-plan.md §6.7 契約4・§6.9、Contracts/Perception（ObservationResult は実装済み契約）

## 何を作ったか

1. **FakeObservations（tests/OpenLogicool.Fakes/FakeObservations.cs・新規）**
   - Known／Ambiguous／Unknown／Unavailable の4状態の ObservationResult を決定的に合成する builder。実画面 capture を参照しない（Phase 4 の「現在 state」根拠は GameLab oracle と fake だけ）。
   - 状態の意味を破る fake が作れない: Ambiguous は異なる2候補（confidence 0.51/0.49＝差が小さい・§6.9「Known へ丸めない」）、Unavailable は理由必須・候補なし、空 ID／空 stateId／空 reason は拒否。
   - **AttemptId を受け取る口が無い**（Perception は Attempt を知らない——§6.7 契約4）。
   - 既存 `FakeObservationSource`（queue 供給・t02 以前から存在)へ4状態を script として流せる。
2. **ContractConformanceSuite.Verify 強化（tests/OpenLogicool.Conformance.Tests・更新）**
   - 追加不変条件: observationId 必須（RunEvent からの参照キー＝「commit 済み Observation」参照の前提）／Ambiguous は候補2件以上／Unavailable は候補を持てない／unavailableReason は Unavailable の時だけ。
   - 既存 fixture 2件（sample=Ambiguous 候補2・gamelab-live=Known 候補1）は強化後も適合（後方互換を実測）。
3. **FakeObservationTests（tests/OpenLogicool.Conformance.Tests・新規）**
   - 4状態すべてが契約適合／Ambiguous の候補差が小さいまま（丸めない）／不正 fake の構築拒否／FakeObservationSource の順序供給／**Perception wire type 全4型と builder に Attempt 参照が構造として存在しない**（reflection）／状態別の契約違反を Verify が拒否（7パターン）。

## Confirmed 契約（「Confirmed には同じ Attempt を参照する commit 済み Observation が必須」）の充足位置

- **束縛の正本**: Playbook が commit する confirmation RunEvent の AttemptId＋ObservationId 併記（§6.7 契約4）。t03 の `RunJournal` が confirmation event の併記を append 時に検証し、t04 の `AttemptDispatchGate.CommitConfirmed` が Observing（observation event commit 済み）経由でだけ Confirmed へ遷移させる——**すでに t03/t04 で成立**。
- t06 はその Observation 供給側を閉じた: fake が observationId を必ず持ち（Verify で必須化）、journal の observation event（ObservationId 必須——t03）に乗る材料として契約適合であること。
- **改善余地（欠陥ではない・note へ記録）**: gate の Observing に入った observation event の ObservationId と confirmation の ObservationId の同一性までは現状検証されない（契約4の文言上は confirmation 併記が成立の正本）。t04 未着地 base のため本 task では触れない。t07 fault matrix／t10 resume 照合の設計材料。

## どう確認したか（focused test・worktree 内で実行）

- `tests/OpenLogicool.Conformance.Tests` **12件 green**（新規6＋既存6。既存6は Verify 強化後の後方互換確認を兼ねる）

## 対象外

- GameLab oracle との状態接続（t08）、journal／gate との統合検証（t04 着地後の面）、実画面 capture／実 recognizer（Phase 5）、confidence calibration（契約文書の未決定のまま）

## 罠・申し送り

- Conformance.Tests は fixture を repo root（OpenLogicool.sln 探索）から読むため、build 出力 redirect 下では既存6件が DirectoryNotFound で落ちる。redirect を外して worktree 内 obj で実行→終了後に obj/bin を削除して観測を clean に保った（architecture test と同じ手法）。
