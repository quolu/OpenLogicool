# Phase 4 Exit Assessment（Durable Automation Lab）

- 作成: 2026-08-20（統括 bell-grok46・t11）
- 上位正本: [development-plan.md](development-plan.md) §Phase 4（Exit 条件の文言はそちらが正）
- campaign 受入: [phase4-campaign-plan.md](phase4-campaign-plan.md)
- 判定材料: Lattice plan `phase4-durable-lab`（t01〜t10 done・t11 は本書）・[evidence/phase4-durable-lab/](../evidence/phase4-durable-lab/)
- 根拠4値: 確認済み（実測あり）／強い推定（構造・対称性による）／未確認／非対応
- 最終 Exit 宣言はオーナー裁定（H）。親は todo done を代行しない

## Exit 6条件の判定

### 条件1: 全 fault point で未解決 DispatchArmed から次 dispatch を自動生成しない — **確認済み**

- 構造: `AttemptDispatchGate.ArmThenDispatch` は `IsUnresolvedAfterArm` な Attempt があると例外。外部入力の再送 loop は無い。製品側に自動継続 executor は無く、`RunControls.StepOnce` は Paused の一手だけ。
- crash 復元: journal が dispatch で途切れると `Recover` が OutcomeUnknown にし、次の `ArmThenDispatch` は拒否（契約5）。保証できる中止だけ `CommitDisarmed`（ActorType=System）。partial SendInput は常に OutcomeUnknown。矛盾した「未呼出保証」は例外。
- 実測: `FaultMatrixTests` が計画 §10.2 の全境界を fixture 化。focused test と 2026-08-20 の full regression が green。
- 注記: `TargetWindowLost` は分類器の写像として閉じ、Recover 専用 scenario は無い。分類結果は Disarmed／OutcomeUnknown に落ちるので不変条件は崩れない。

### 条件2: Confirmed に Observation が必ず存在する — **確認済み（ObservationId 必須）／強い推定（Observing event との同一性）**

- 構造（確認済み側）: `DurableAttempt.TransitionTo(Confirmed)` と `Restore(Confirmed)` は ObservationId なしを拒否。journal の confirmation は AttemptId＋ObservationId 併記必須（§6.7 契約4）。live の `CommitConfirmed` は Observing 経由だけ。Perception／`FakeObservations` は AttemptId を受け取らない。
- 計画 §6.7 607行は「契約4は confirmation RunEvent の併記だけで成立」と読む。この読みでは Confirmed に ObservationId が無い経路は型と journal の両方で閉じている。
- 同一性（強い推定に残す側）: `CommitConfirmed` も `Recover` も、Observing に入った observation event の ObservationId と confirmation の ObservationId を比較しない。`AttemptDispatchGateTests.Recover_classifies_attempts_from_journal_events_only` は observation event 既定値 `observation-1` に対し confirmation を `observation-done` として Confirmed を合法にしている。store へ confirmation を直接積めば observation event 無しでも Recover は Confirmed を作れる。
- 判定を全部確認済みに上げない。契約4の弱い読み（併記）は満たし、Exit 文言の強い読み（同じ Attempt の commit 済み Observation event と同一）は未閉鎖。実装はしない（本 task は材料）。残課題へ記録する。

### 条件3: journal replay と projection が一致する — **確認済み**

- 構造: `RunProjection.Replay` は `FromFirstEvent`＋`Apply` の fold。`SessionRecorder.Record` は projection 計算→journal append→確定の順で、どちらかが落ちた event は両方に現れない。`SessionReplayer` は読み取り専用。
- 実測: 同一 event 列の逐次 Apply と Replay の値等価、recorder projection と replayer の全 Run 一致、disarm 込み fault 列でも一致。t03 が SQLite 再 open の journal 復元を Persistence focused test で実証済み。
- 注記: SQLite 再 open と `RunProjection` の結合試験は無い。store 忠実性（t03）と fold 等価（t09／t07）の合成で閉じる。

### 条件4: active Run の version が crash や edit で勝手に変わらない — **確認済み**

- 構造: pin と違う version を運ぶ event は `RunProjection.Apply` が拒否する。pin が動くのは payload が `version-switch` のときだけ（PB-007 の明示切替）。編集は `PlaybookCorrection` が新 version を作り、実行済み event は変えない。`SwitchVersion` は Paused＋再観察＋graph 互換のときだけ。
- crash: `SessionRecorder.Restore` は store の実 event の replay だけ。復元 API が version を書き換える口は無い。switch の journal append 失敗では pin も replay も旧 version のまま（`FaultMatrixTests` 境界9）。
- version-switch は「勝手に変わる」抜け穴ではない。journal に入った明示切替を replay が追うのが仕様。

### 条件5: manual intervention 後は再観察なしに進まない — **強い推定**

- プロセス内（確認済みに近い）: `RunControlState.Resume` は `NeedsReobservation` で例外。`StepOnce`／`Skip`／`SwitchVersion`／`CommitAttemptObserving` も同じ。介入中の observation は run-level と attempt 束縛の両方で拒否。`ResumeReadiness.SatisfiesReobservation` は最後の intervention より後の、指定 ObservationId の observation event だけ真。介入開始だけで crash した run は偽。
- 落とせた点: `RunControls` に journal からの再構築が無い。コンストラクタは常に `RunControlState.Start()`（Running・再観察待ちなし）。crash 後に host がこれで組んで `StepOnce` すると、journal 上は再観察未充足でも進行できる。`ResumeGate` は journal 事実を見るが `RunControls.Resume` には繋がっていない。
- 連続実行 executor はまだ無いので、今の製品経路で自動進行は起きない。それでも Exit 文言は「進まない」であり、復元口が flag を捨てる以上、確認済みには上げない。

### 条件6: 現在 state は GameLab oracle／fake Observation に限る。実画面 resume claim は使わない — **確認済み**

- 構造: `GameLabStatusInput` に capture field が無い。`GameLabRunConsole.ReportObservation` は `ObservationStatus` だけ。製品 GameLab の csproj 参照は Contracts＋Playbooks のみ（AI／Capture／Devices／Input なし。focused test が参照を固定）。
- `ResumeGate` は Observation の出所を知らず、実画面 UniqueMatch の成立主張を Phase 5 に切っている。検証は fake／合成データだけ。
- fake が `CaptureBackend.WindowsGraphicsCapture` をラベルとして仮装するが pull ではない。resume claim ではない。
- UX-005 の GameLab 画面配線は無い（`ResumeReportView` は pure 値まで）。画面が無いことは偽の実画面 claim ではない。campaign H の目視は別項。

## campaign 受入条件（§Phase 4 Exit 以外）

| # | 条件 | 判定 |
|---|---|---|
| 1 | assessment が4値で揃っている | 本書 |
| 2〜7 | Exit 6条件と同じ | 上記 |
| 8 | 各 ToDo は focused test green＋証跡で閉じ、対象限定 commit・push 済み | **確認済み**（t01〜t10。room 監査クローズ＋着地 commit） |
| 9 | 未成立は未成立と明記 | 条件2の同一性・条件5の復元を隠していない |

## full regression

2026-08-20（t11・HEAD `53ca53f` 時点）: `dotnet test OpenLogicool.sln` — 15 test project・計 **532** 件、失敗 0。

| プロジェクト | 件数 |
|---|---|
| Architecture | 4 |
| GameLab.Prototype | 3 |
| Desktop.SmokeApp | 2 |
| Domain | 87 |
| Playbooks | 94 |
| Input | 79 |
| Profiles | 22 |
| Desktop | 58 |
| GameLab | 18 |
| Conformance | 12 |
| Devices.G600 | 59 |
| Probe | 6 |
| Devices.G13 | 10 |
| Host | 49 |
| Persistence | 29 |

## Grok read-only 監査

2026-08-20（親直読＋`refuter` 1席。Codex は本環境 sandbox 破損のため使わない。円卓監査席は 2026-08-19 leave-seat 済みで立て直していない）。

- **重大（Exit の4値を動かす）**: 2件。いずれも実装せず、上の条件2・5に反映した。
  1. Recover／`CommitConfirmed` が Observing の ObservationId と confirmation の ObservationId を同一視しない。不一致 Recover がテストで合法。
  2. `RunControls` に journal からの再観察復元が無い。条件5はプロセス内だけ。
- **軽微**: GameLab console と kernel（RunControls／ResumeGate）が未接続。`GameLabRunConsole.Resume` は再観察を見ない bool。SQLite×projection 結合試験なし。fake の WGC ラベル。`TargetWindowLost` の Recover scenario 欠落。
- 反証は「6条件すべて確認済み」を殺した。材料としてオーナー裁定に出せる、が結論。

## オーナー手番（H）

1. **Phase 4 Exit 裁定**（本書を材料に宣言する／差し戻す）
2. **GameLab 停止／再開面の目視** — 未実施。t08 は WPF 窓を足していない（「UI は最後」）。目視対象は console／projector の API 面だけ。Exit 6条件の blocker には数えない。

G600 残置の実機確認は本 campaign 外（[g600-leftover-operation.md](g600-leftover-operation.md)）。

## 残課題（Exit 判定外・次フェーズ以降）

- Observing と confirmation の ObservationId 同一性（Recover 含む）
- `RunControls` の journal 再構築（`SatisfiesReobservation` を捨てない）
- UX-005 の GameLab 描画、Disarmed の表示語彙、GameLab pause と RunControls の配線
- 実画面 UniqueMatch 再開（Phase 5）
- UI 保存と関連付けの導線統合（Phase 3 持ち越し）
- 連続実行 executor を将来置くなら、gate の契約5と `NeedsReobservation` の両方を通す

## 判定要旨

Exit 6条件は **確認済み 4**（条件1・3・4・6）／**確認済み＋強い推定 1**（条件2）／**強い推定 1**（条件5）。未確認・非対応の条件は無い。未閉鎖の2点は成功扱いにしていない。最終 Exit 宣言はオーナー裁定。
