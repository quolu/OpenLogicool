# Phase 4 Exit Assessment（Durable Automation Lab）

- 作成: 2026-08-20（統括 bell-grok46・t11）
- 上位正本: [development-plan.md](development-plan.md) §Phase 4（Exit 条件の文言はそちらが正）
- campaign 受入: [phase4-campaign-plan.md](phase4-campaign-plan.md)
- 判定材料: Lattice plan `phase4-durable-lab`（t01〜t10 done・t11 は本書）・[evidence/phase4-durable-lab/](../evidence/phase4-durable-lab/)
- 根拠4値: 確認済み（実測あり）／強い推定（構造・対称性による）／未確認／非対応
- Exit 宣言: **2026-08-20 親（bell-grok46）**。技術成立の宣言でオーナーに止めない（工場 H は高リスク操作の明示承認であり、Phase gate ではない）

## Exit 6条件の判定

### 条件1: 全 fault point で未解決 DispatchArmed から次 dispatch を自動生成しない — **確認済み**

- 構造: `AttemptDispatchGate.ArmThenDispatch` は `IsUnresolvedAfterArm` な Attempt があると例外。外部入力の再送 loop は無い。製品側に自動継続 executor は無く、`RunControls.StepOnce` は Paused の一手だけ。
- crash 復元: journal が dispatch で途切れると `Recover` が OutcomeUnknown にし、次の `ArmThenDispatch` は拒否（契約5）。保証できる中止だけ `CommitDisarmed`（ActorType=System）。partial SendInput は常に OutcomeUnknown。矛盾した「未呼出保証」は例外。
- 実測: `FaultMatrixTests` が計画 §10.2 の全境界を fixture 化。focused test と 2026-08-20 の full regression が green。
- 注記: `TargetWindowLost` は分類器の写像として閉じ、Recover 専用 scenario は無い。分類結果は Disarmed／OutcomeUnknown に落ちるので不変条件は崩れない。

### 条件2: Confirmed に Observation が必ず存在する — **確認済み**

- 構造: Observing は ObservationId 必須。Confirmed は Observing と同じ ID だけを受け付ける。`CommitConfirmed` は同じ Attempt の commit 済み observation event と ID が一致しなければ journal へ書かない。`Recover` は confirmation があっても一致する observation event が無ければ例外（Confirmed へ丸めない）。
- 実測: Domain／Playbooks focused test（不一致 live・不一致 Recover・observation 無し Recover を含む）。2026-08-20 修正（親直轄）。

### 条件3: journal replay と projection が一致する — **確認済み**

- 構造: `RunProjection.Replay` は `FromFirstEvent`＋`Apply` の fold。`SessionRecorder.Record` は projection 計算→journal append→確定の順で、どちらかが落ちた event は両方に現れない。`SessionReplayer` は読み取り専用。
- 実測: 同一 event 列の逐次 Apply と Replay の値等価、recorder projection と replayer の全 Run 一致、disarm 込み fault 列でも一致。t03 が SQLite 再 open の journal 復元を Persistence focused test で実証済み。
- 注記: SQLite 再 open と `RunProjection` の結合試験は無い。store 忠実性（t03）と fold 等価（t09／t07）の合成で閉じる。

### 条件4: active Run の version が crash や edit で勝手に変わらない — **確認済み**

- 構造: pin と違う version を運ぶ event は `RunProjection.Apply` が拒否する。pin が動くのは payload が `version-switch` のときだけ（PB-007 の明示切替）。編集は `PlaybookCorrection` が新 version を作り、実行済み event は変えない。`SwitchVersion` は Paused＋再観察＋graph 互換のときだけ。
- crash: `SessionRecorder.Restore` は store の実 event の replay だけ。復元 API が version を書き換える口は無い。switch の journal append 失敗では pin も replay も旧 version のまま（`FaultMatrixTests` 境界9）。
- version-switch は「勝手に変わる」抜け穴ではない。journal に入った明示切替を replay が追うのが仕様。

### 条件5: manual intervention 後は再観察なしに進まない — **確認済み**

- プロセス内: `RunControlState.Resume` は `NeedsReobservation` で例外。`StepOnce`／`Skip`／`SwitchVersion`／`CommitAttemptObserving` も同じ。介入中の observation は run-level と attempt 束縛の両方で拒否。
- 復元: event の無い新規 Run だけ `Start()`＝Running。既存 journal は `FromJournal` で再構築する——abandon は Abandoned、介入 event が奇数個なら介入中、偶数個で終了後に observation が無ければ再観察待ち、それ以外は Paused（pause は journal に無いので Running へ戻さない）。
- 実測: `FromJournal` の各分岐、再構築後の Resume／StepOnce 拒否、介入中の観測拒否。2026-08-20 修正（親直轄）。

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

2026-08-20（修正前 HEAD `53ca53f`）: 15 project・532 件、失敗 0。

修正後（同日・Exit 宣言直前）: `dotnet test OpenLogicool.sln` — 15 project・計 **540** 件、失敗 0（Domain 90・Playbooks 99 を含む）。

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

- **重大**: 当初2件（ObservationId 非同一性、RunControls が journal を捨てて Start する）は 2026-08-20 に親が直し、focused test で閉じた。残る重大なし。
- **軽微**: GameLab console と kernel（RunControls／ResumeGate）が未接続。`GameLabRunConsole.Resume` は再観察を見ない bool（表示面。dispatch は RunControls）。SQLite×projection 結合試験なし。fake の WGC ラベル。`TargetWindowLost` の Recover scenario 欠落。

## 閉じた手番

- GameLab 停止／再開の WPF 目視は対象窓が無い（「UI は最後」）。API／focused test で閉じ、目視待ちで止めない。
- G600 残置の実機確認は本 campaign 外（[g600-leftover-operation.md](g600-leftover-operation.md)）。

## 残課題（Exit 判定外・次フェーズ以降）

- UX-005 の GameLab 描画、Disarmed の表示語彙、GameLab pause と RunControls の配線
- 実画面 UniqueMatch 再開（Phase 5）
- UI 保存と関連付けの導線統合（Phase 3 持ち越し）
- 連続実行 executor を将来置くなら、gate の契約5と `NeedsReobservation` の両方を通す

## 判定要旨

Exit 6条件は **確認済み 6**。未確認・非対応の条件は無い。**Phase 4 Exit 成立**（2026-08-20 親宣言）。
