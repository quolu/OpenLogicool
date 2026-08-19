# t04-attempt-sm 証跡 — Attempt 状態機と DispatchArmed（PB-003/004/005）

- 実施: 2026-08-19（hotaru・pull run `phase4-durable-lab-20260819-122051`・worktree base ce4cb20＝t03 着地後）
- 仕様正本: docs/development-plan.md PB-003／004／005・§6.7（状態機と契約1〜8）、docs/phase4-campaign-plan.md §t04、t03 の journal（RunJournal／IRunJournalStore・base に着地済み）

## 何を作ったか

1. **AttemptState（Contracts/Playbooks/AttemptContracts.cs・新規）**
   - §6.7 の16状態の enum。wire・journal・表示の語彙だけを提供し、遷移の許可は Domain が持つ。
2. **DurableAttempt（Domain/DurableAttempt.cs・新規・pure）**
   - §6.7 遷移図の忠実な写し（静的遷移表）。表に無い遷移・終端からの遷移は例外。
   - Confirmed への遷移・復元は ObservationId 必須（契約4）。Confirmed 以外への observationId 指定は拒否。
   - Input API の戻り値を受け取る口が無い（契約3を型で保証）。
   - `RecoveryStateFor`（契約2）: 終端→維持、Proposed/Authorized/Prepared→Cancelled、DispatchArmed 以降の非終端→OutcomeUnknown（実際に未送信でも）。
   - `IsUnresolvedAfterArm`: dispatch し得た後の未解決判定（契約5の監視対象）。
   - `Restore` は復元専用の実体化口（OPS-008）。
3. **AttemptDispatchGate（Playbooks/AttemptDispatchGate.cs・新規）**
   - **PB-003**: `ArmThenDispatch(dispatchEvent, externalInput)` が dispatch event（DispatchArmed の commit・AttemptId＋CommandId は t03 journal が検証)を journal へ append し、**成功した後にだけ**外部入力 delegate を呼ぶ。journal append 失敗時は外部入力に到達しない（構造で強制）。
   - **PB-004**: 外部入力の例外はそのまま伝播し、Attempt は DispatchArmed のまま未解決として残る。再送 loop・retry を持たない。
   - **PB-005／契約6・7**: journal append と外部入力は別ステップで、transaction を共有する口が無い。外部入力が失敗しても commit 済み dispatch event は巻き戻らない。
   - **契約3**: 外部入力の成功で状態は進まない。DispatchReported へ進めるのは `CommitReported`（dispatch-result event）だけ。
   - **契約5**: `IsUnresolvedAfterArm` な Attempt が居る間、次の `ArmThenDispatch` を拒否。終端への解決後だけ次が通る。
   - **契約8**: 登録済み AttemptId の再利用（CommitProposed の再実行）を拒否——前提が変わったら新 AttemptId で作り直す。
   - **契約2／OPS-008**: `Recover(store, journal)` が journal の実 event だけから再分類——confirmation あり→Confirmed（ObservationId は event から）、dispatch あり未確定→OutcomeUnknown、dispatch 前→Cancelled。journal に記録の無い解決は信じず未解決へ倒す（安全側）。復元された OutcomeUnknown も契約5 の拒否対象。
   - `ResolveLocally`: 外部効果を伴わない遷移の台帳反映（journal 非書込。解決 event の journal 表現は t05 run controls／t07 fault matrix で確定するため、この task では所有しない）。
   - Attempt の commit（PB-003 前段）: `CommitProposed`（proposal）／`CommitAuthorized`（approval）が journal commit と対で登録・遷移。Prepared は契約2 で Cancelled へ倒れる領域のため journal event を持たない gate 内遷移。

## どう確認したか（focused test・worktree 内で実行）

- `tests/OpenLogicool.Domain.Tests` **43件 green**（DurableAttemptTests 追加33: 正常経路の Confirmed 到達と観測束縛／Confirmed の観測必須／観測を受けるのは Confirmed だけ／**終端7状態×全16遷移の全拒否**／飛ばし遷移拒否／OutcomeUnknown→Reconciling→NeedsUserDecision→UserResolved 経路（利用者解決は Observation を持たない）／Disarmed は DispatchArmed からのみ／**RecoveryStateFor の契約2分類12ケース**／Confirmed 復元の観測必須／IsUnresolvedAfterArm 境界7ケース）
- `tests/OpenLogicool.Playbooks.Tests` **44件 green**（AttemptDispatchGateTests 追加12: 全経路一巡（6 event・観測束縛）／**外部入力呼出時点で dispatch event が journal に在る**（PB-003 順序）／**journal 失敗→外部入力未到達・Prepared 維持**／**外部入力失敗→呼出1回のみ・DispatchArmed 維持・dispatch event 非巻き戻し**（PB-004/005）／**外部入力成功でも状態不変**（契約3）／**未解決中の次 dispatch 拒否→解決後に許可**（契約5）／Observing 外からの confirmation 拒否／AttemptId 再利用拒否（契約8）／payload type 不一致・AttemptId 欠落拒否（journal 未書込）／ResolveLocally の journal 非書込／**Recover の3分類**（Cancelled／OutcomeUnknown／Confirmed＋観測復元）／**復元後の OutcomeUnknown が次 dispatch を拒否**（OPS-008×契約5））
- `tests/OpenLogicool.Architecture.Tests` **4件 green**（依存規則不変——Persistence 変更なし・migration 追加なし）

計 91件 green（focused のみ。通し試験は Exit の最終確認まで行わない）。

## 対象外（scope に含めない）

- 解決 event（Rejected／Disarmed／OutcomeUnknown 等）の journal 表現（t05 run controls・t07 fault matrix が確定）
- pause／skip／abandon／manual intervention（t05）、fake Observation の4値（t06）、全 fault point の網羅（t07）
- 実 SendInput への接続（外部入力は delegate 境界まで。fast path 純潔は不変——gate は Playbooks 側で fast path に居ない）

## 罠・申し送り

- 一時 `Directory.Build.props`（build 出力の worktree 外 redirect・room [15] と同じ手法）を使用し、commit には含めず削除済み。architecture test は redirect を外して単独実行→生成物即削除。
- witness 入力の置き場は `.lattice/todo/witness/`（`.lattice/runs/` 直下に置くと run store が INVALID_RUN_STORE になる——bell [14] の実被弾）。
