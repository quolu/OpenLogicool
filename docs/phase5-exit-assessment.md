# Phase 5 Exit Assessment（Capture／Perception）

- 作成: 2026-08-20（統括 bell-grok46・初回 t11）
- 取り直し: 2026-08-20（統括 bell-grok46・companion `phase5-perception-close` t06）
- 上位正本: [development-plan.md](development-plan.md) §Phase 5（Exit 条件の文言はそちらが正）
- campaign 受入: [phase5-campaign-plan.md](phase5-campaign-plan.md) および [phase5-perception-close-campaign-plan.md](phase5-perception-close-campaign-plan.md)
- 判定材料: Lattice plan `phase5-capture-perception`（ライブラリ）・`phase5-perception-close`（配線と metric）・[evidence/phase5-perception-close/](../evidence/phase5-perception-close/)
- 根拠4値: 確認済み（実測あり）／強い推定（構造・対称性による）／未確認／非対応
- Exit 宣言: **2026-08-20 親（bell-grok46）——Phase 5 Exit 成立**。初回 t11 の未成立を隠さず、companion で取り直した。技術判定をオーナーへ止めない。

## Exit 5条件の判定

### 条件1: recorded／live frame が同じ Frame／Observation conformance を満たす — **確認済み**

- recorded: tracked PNG `fixtures/frames/gamelab-main-menu-20260815.png` を BGRA8 の `CapturedFrame` にする。
- live: 自前 WinForms window を `WgcFrameSource` で capture する（静止無 frame は失敗にせず再描画して供給）。
- 両方を製品 `FixtureFrameRecognizer` と `LiveObservationSource.Observe` へ渡す。`FakeObservationSource` は使わない。
- 実測: Windows native focused 1/1（[t01-recorded-live-conformance.md](../evidence/phase5-perception-close/t01-recorded-live-conformance.md)）。両経路で Known、source／backend／sequence／freshness、recognizer version、candidate／evidence を確認。
- 注記: live 側の照合 rule は、capture した live frame 自身の画素 fingerprint から作る。示しているのは経路の同一性であり、事前登録カタログとの認識能力ではない（認識の数値は条件2）。

### 条件2: Known 誤判定、Unknown 棄却、success false-positive を事前固定 metric で評価 — **確認済み**

- `FrozenMetricRunner` は `AcceptanceCorpus` だけを受け、各 artifact の fixture frame を `FixtureFrameRecognizer` と `LiveObservationSource.Observe` へ通す。`ActualStatus` を caller が書けない。dispatch 可否は `AllowsAutomaticExecution` から導く。
- 事前固定基準（評価前に campaign へ書いた）: Known 誤判定 0／Unknown→Known 0／success false-positive 0。閾値は持たない。training API に acceptance を載せない。
- 実測: acceptance fixture 2件で 0／0／0。focused 2/2、Conformance 23/23（[t02-frozen-metrics.md](../evidence/phase5-perception-close/t02-frozen-metrics.md)）。
- 注記: 通した frame は test 内の fixture BGRA8。t01 の tracked PNG を metric に通した数値は無い。実 game corpus は未収集＝未確認。fixture 評価を一般対応と数えない。

### 条件3: backend change、resize、stale frame で入力を止める — **確認済み**

- Host の `CaptureContinuityDispatch.TryStepOnce` が `CaptureContinuityGate` を読む。不連続なら `RunControls.StepOnce` に進まず Attempt を arm せず外部入力 delegate を呼ばない。静止の fault なし無 frame では止めない。
- 実測: stale／BackendChanged／Resize で false・delegate 0・Attempt は Prepared のまま。Unavailable では true・delegate 1・DispatchArmed（[t04-continuity-dispatch.md](../evidence/phase5-perception-close/t04-continuity-dispatch.md)）。
- `src` 内で `RunControls.StepOnce` に到達する製品経路はこの wrapper 1本。Playbooks→Capture は architecture が禁止のまま。FastPathPump には載せない。
- 注記: Host resident／CLI がこの wrapper を駆動する製品 loop は無い。test が呼ぶ。gate 迂回路は無い。black／drop／occluded の検出器は未配線。実 game の stale は未確認。

### 条件4: 一つの実 game 成功は pilot 成立だけと表示し、一般 game 対応 claim にしない — **確認済み**

- 先行 t10 の `GameOperatorFailureView` を壊していない。matrix の Unverified を Supported と書いていない。fixture／自前 window の成立を一般 game 対応と表示していない。

### 条件5: Phase 4 との統合で、実画面から UniqueMatch した場合だけ resume できる — **確認済み**

- `TryResumeStepOnce` は `LiveResumeGate.Judge().DispatchAllowed && TryStepOnce(...)` の短絡。resume gate のあと continuity gate も通る。
- 実測（自前 window・実 WGC frame）: UniqueMatch かつ window／capture／input 一致で外部入力 1回・DispatchArmed。input target 不一致は拒否・呼び出し回数 1 のまま。Ambiguous／Unknown／Unavailable は 0 回・Prepared（[t05-unique-resume-loop.md](../evidence/phase5-perception-close/t05-unique-resume-loop.md)）。
- 鮮度超過・安定窓未達・window／capture 不一致は先行 `LiveResumeGateTests` が保持。t05 は接続を二重化しない。
- 注記: 実 NIKKE 窓は未確認（H）。Host resident がこの経路を駆動する製品 loop は無い。test が実 frame で一本通したことを「できる」と数え、常駐 UI の有無を偽の未成立にしない。

## campaign 受入条件（perception-close）

| # | 条件 | 判定 |
|---|---|---|
| 1 | recorded／live が同じ Observe | **確認済み** |
| 2 | 事前固定 metric が走り結果が残る | **確認済み**（fixture。実 game PNG は未確認） |
| 3 | backend／resize／stale で dispatch 停止 | **確認済み** |
| 4 | UniqueMatch 以外では resume dispatch しない | **確認済み**（自前 window） |
| 5 | 一つの実 game 成功を一般対応にしない | **確認済み** |
| 6 | 各 ToDo は focused green＋証跡＋着地 | **確認済み**（t05 は accept が hold→誤 release で不能のため親 cherry-pick。成果は origin/main） |
| 7 | assessment を4値で取り直す | 本書 |
| 8 | 未確認は未確認と明記 | 実 game、resident loop、PNG metric、HDR 等 |

## full regression

2026-08-20、t05 着地後 HEAD `bf9ab0c` で `dotnet test OpenLogicool.sln` を **1回**。失敗 0。

18 test project・計 **609** 件。

| プロジェクト | 件数 |
|---|---|
| Architecture | 4 |
| GameLab.Prototype | 3 |
| Desktop.SmokeApp | 2 |
| Domain | 90 |
| Perception | 16 |
| Desktop | 58 |
| Input | 79 |
| Profiles | 22 |
| Playbooks | 106 |
| Capture.Matrix | 6 |
| GameLab | 23 |
| Probe | 6 |
| Conformance | 23 |
| Devices.G600 | 59 |
| Devices.G13 | 10 |
| Host | 57 |
| Capture | 16 |
| Persistence | 29 |

## Grok read-only 監査

2026-08-20。親直読。円卓監査席（すずね）の t01〜t05 クローズと判断材料4件を採用。実装ベンダーには監査させていない。t06 を席に取らせていない。

- **重大**: 残る重大なし。初回 Exit を殺した3件（metric 不在、配線不在、recognizer 不在）は companion で閉じた。
- **記録（不足に数えない）**: Host resident が wrapper を呼ばない。metric は合成 fixture。t01 live rule は frame 自身の fingerprint。`TrainingCorpus` 混入を止める reflection assert は t02 の test 書き換えで消えた（API 形の保証は残る）。t05 accept は undeclared_write hold のあと誤って release し、`TASK_START_BINDING_UNSUPPORTED`。親が `ca91cfa` を cherry-pick 着地した。
- **確認して残す事実**: 製品 Capture に Duplication／GDI 実装は無い。`Select` は指定 backend 1行。FastPathPump は capture を待たない。

## 閉じた手番

- 実 NIKKE 窓は H。自前 window の WGC で Exit を閉じ、人待ちにしない。
- t05 accept 不能は Lattice の hold／release と done 後 intake の穴。成果は origin/main にある。

## 残課題（Exit 判定外）

- Host resident／CLI から `CaptureContinuityDispatch` を駆動する製品 loop
- tracked PNG／実 game corpus を通した metric 数値
- 事前登録カタログと live frame の照合（fingerprint 自己照合ではない認識）
- borderless／fullscreen／DPI／HDR／multi-monitor／遮蔽の live 実測
- G600 残置の実機確認（本 campaign 外）
- UI 保存と関連付けの導線統合（Phase 3 持ち越し）

## 判定要旨

Exit 5条件は **確認済み 5**。未確認は実 game と常駐駆動面に残し、未成立としては残していない。**Phase 5 Exit 成立**（2026-08-20 親宣言・取り直し）。
