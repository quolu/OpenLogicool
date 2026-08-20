# Phase 5 Exit Assessment（Capture／Perception）

- 作成: 2026-08-20（統括 bell-grok46・t11）
- 上位正本: [development-plan.md](development-plan.md) §Phase 5（Exit 条件の文言はそちらが正）
- campaign 受入: [phase5-campaign-plan.md](phase5-campaign-plan.md)
- 判定材料: Lattice plan `phase5-capture-perception`（t01〜t10 done・t11 は本書）・[evidence/phase5-capture-perception/](../evidence/phase5-capture-perception/)
- 根拠4値: 確認済み（実測あり）／強い推定（構造・対称性による）／未確認／非対応
- Exit 宣言: **2026-08-20 親（bell-grok46）——Phase 5 Exit は未成立**。技術判定をオーナーへ止めない。未成立を成立と書かない。

## Exit 5条件の判定

### 条件1: recorded／live frame が同じ Frame／Observation conformance を満たす — **未成立**

- 契約: `CapturedFrame` と `ObservationResult` の wire／conformance fixture は存在する。`LiveObservationSource.Observe` は recorded／live を区別せず一つの入口に正規化する。
- 実測されているもの: Windows native の自前 window から `WgcFrameSource.Pull()` で BGRA8 frame（t01）。合成 `CapturedFrame` を `LiveObservationSource` に渡した4状態正規化（t06 focused test 9件）。JSON fixture の Observation deserialize conformance。
- 欠け: recorded fixture を WGC 経路で replay して同一 Observation を取る試験は無い。t06 の「recorded and live share four-state」は両方ともテスト内の合成 `Frame()`。`FakeObservationSource` は渡された frame を捨てて queue を返す。WGC 画素 → `LiveObservationSource` の結合は Host／製品ループに存在しない。
- 同じ `Observe()` 入口という構造は **強い推定** の材料であり、Exit 条件の「recorded／live frame が同じ conformance を満たす」実測には足りない。

### 条件2: Known 誤判定、Unknown 棄却、success false-positive を事前固定 metric で評価 — **未成立**

- あるもの: `CorpusPartition` が development／calibration／acceptance を型で分離し、training API は acceptance を表現できない（t08）。出典必須。
- 無いもの: confusion matrix、閾値、frozen 評価 runner、Known 誤判定率、Unknown 棄却率、success false-positive 率。製品 `IFrameRecognizer` 実装は interface と test fake だけ。実 corpus 収集は t08 自身が未確認。
- 口だけ用意して評価していない状態を「metric で評価した」と数えない。

### 条件3: backend change、resize、stale frame で入力を止める — **未成立**

- モジュール: `CaptureContinuityGate` は fault／backend 変更／transform revision 変更／stale で `AllowsAutomaticInput=false` にする。静止の無 frame は切らない。focused test と WGC の resize→`Resize` fault は **確認済み**。
- 製品: `AllowsAutomaticInput` を Host／Playbooks dispatch／Input Emitter から参照する経路は無い（self-test 以外ゼロ）。fast path は設計どおり capture を待たない。gate の bool は入力 API を止めない。
- WGC が製品で出す fault は Resize／Minimized が主。black／drop／occluded の検出器は未配線。Desktop Duplication／GDI への黙った fallback は製品 Capture に無い（非採用・matrix 単一行）。fallback が無いことは本条件の成立根拠にしない。

### 条件4: 一つの実 game 成功は pilot 成立だけと表示し、一般 game 対応 claim にしない — **確認済み**

- `GameOperatorFailureView` は Unverified／Unsupported のとき「一つの実 game で成功しても一般対応とは表示しません」を返す。capture 失敗の次手は「別の取得方式へは自動で切り替えません」。focused test 23件中にこの表示を含む（t10）。
- 計画・証跡・matrix は borderless／fullscreen／HDR 等を Unverified のまま残し、Supported と書いていない。
- 注記: Host／Desktop の常駐画面には未配線。未配線は偽の一般対応 claim ではない。実 game 成功そのものは本 Phase に無い。

### 条件5: Phase 4 との統合で、実画面から UniqueMatch した場合だけ resume できる — **未成立**

- モジュール: `LiveResumeGate.Judge` は UniqueMatch 以外、stale、安定窓未達、window／capture source／input target 不一致で `DispatchAllowed=false`。InputEmitter を参照しない。focused test（Playbooks）で **確認済み**。
- 製品: Host／GameLab runtime／`AttemptDispatchGate` から `LiveResumeGate` を呼ぶ経路は無い。実画面 UniqueMatch を出す製品 recognizer も無い。t09 証跡自身が実機の window×capture×input 同時照合を未確認としている。
- UniqueMatch 以外を拒むライブラリは存在する。実画面から UniqueMatch して resume した事実は無い。拒む口があることを「できる」と読まない。

## campaign 受入条件

| # | 条件 | 判定 |
|---|---|---|
| 1 | assessment が4値で揃っている | 本書 |
| 2 | recorded／live 同一 conformance | **未成立**（条件1） |
| 3 | 事前固定 metric 評価 | **未成立**（条件2） |
| 4 | backend／resize／stale で入力停止 | **未成立**（条件3） |
| 5 | 一つの実 game 成功を一般対応にしない | **確認済み**（条件4） |
| 6 | 実画面 UniqueMatch のときだけ resume | **未成立**（条件5） |
| 7 | 各 ToDo は focused test green＋証跡で閉じ、対象限定 commit・push 済み | **確認済み**（t01〜t10 着地。t02 は sln `undeclared_write` で accept 不能のため cherry-pick 着地。成果は origin/main にある） |
| 8 | 未成立は未成立と明記 | 本書。条件1・2・3・5を隠していない |

## full regression

2026-08-20、`OpenLogicool.Perception.Tests` を sln 登録した HEAD `e6e7e44` で `dotnet test OpenLogicool.sln` を **1回**。失敗 0。

18 test project・計 **591** 件。

| プロジェクト | 件数 |
|---|---|
| Architecture | 4 |
| GameLab.Prototype | 3 |
| Desktop.SmokeApp | 2 |
| Domain | 90 |
| Perception | 9 |
| Desktop | 58 |
| Input | 79 |
| Profiles | 22 |
| Playbooks | 106 |
| Capture.Matrix | 6 |
| GameLab | 23 |
| Probe | 6 |
| Conformance | 21 |
| Devices.G600 | 59 |
| Devices.G13 | 10 |
| Host | 49 |
| Capture | 15 |
| Persistence | 29 |

Perception.Tests の sln 登録は t06 が宣言境界を広げずに残した作業で、本 t11 が regression 前に着地させた（commit `e6e7e44`）。

## Grok read-only 監査

2026-08-20。親直読＋円卓外 `refuter` 1席（`spawn_subagent` は円卓ではない）。実装ベンダー（Codex／Terra）には監査させていない。

- **重大（Exit 成立を殺す）**: ①frozen metric／confusion matrix／FP eval 不在 ②WGC→Observation→ContinuityGate→LiveResumeGate→dispatch 未配線 ③製品 recognizer 不在のため実画面 UniqueMatch を構造的に出せない。
- **確認して残す事実**: 製品 Capture に Duplication／GDI 実装は無く、`CaptureCapabilityMatrix.Select` は指定 backend 1行だけで fallback しない。
- **軽微**: Host が Capture／Perception を csproj 参照するがソース使用ゼロ。`LiveObservationSource.AllowsAutomaticExecution` は Known のみで UniqueMatch より弱い。ContinuityGate と ResumeGate の鮮度時計が別。`LiveResumeGateTests` の Observation は evidence 空で `LiveObservationSource.Validate` を迂回。

## 閉じた手番

- t11 は assessment。欠けた配線と metric runner を本 task で実装して Exit を買い直さない。それは次 campaign の範囲。
- 実 game 窓の用意は H（人が機械を動かさないと取れない観測）。本判定は人が動かしても製品ループが無いので、人待ちにしない。
- t02 accept 不能は Lattice の manifest 更新穴。成果は cherry-pick 済み。製品穴ではない。

## 残課題（次フェーズ）

- 事前固定の acceptance corpus で Known 誤判定／Unknown 棄却／success FP を測る runner
- 製品 `IFrameRecognizer`（calibration 済み）
- Host または Game Operator runtime で WGC → Observation → ContinuityGate → LiveResumeGate → dispatch を一本化する（fast path には載せない）
- recorded fixture を WGC／同一 Observe 経路で replay する conformance
- borderless／fullscreen／DPI／HDR／multi-monitor／遮蔽の live 実測（matrix は Unverified のまま）
- G600 残置の実機確認（本 campaign 外）

## 判定要旨

Exit 5条件は **確認済み 1**（条件4）／**未成立 4**（条件1・2・3・5）。**Phase 5 Exit は未成立**（2026-08-20 親宣言）。campaign の ToDo は t11 で閉じる。未成立を次 Phase の前提に持ち込む。
