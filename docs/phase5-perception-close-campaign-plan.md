# Phase 5 campaign — Perception close

- status: **active**（2026-08-20 起票。Phase 5 Exit 未成立の残りを閉じる）
- 起票: 2026-08-20（オーナー指示「すすめて。Lattice工程更新して円卓できるわね」）
- 統括: ベル（本セッション親は Grok 4.6）。実装 Terra×high（Codex）／監査 Claude opus×medium（Grok 席固着と Fable 5 quota 切れの実績を継承。実装と監査は別ベンダー）
- 実行 TODO の正本: **Lattice plan `phase5-perception-close`**（本書は目的・思想・非目標・受入条件だけを所有し、ToDo を二重化しない）
- 上位正本: [development-plan.md](development-plan.md) §Phase 5、[phase5-exit-assessment.md](phase5-exit-assessment.md)、先行 campaign [phase5-campaign-plan.md](phase5-campaign-plan.md)
- 先行: Phase 5 のライブラリ（WGC Frame、ContinuityGate、LiveObservationSource、LiveResumeGate、CorpusPartition）は着地済み。本 campaign はそれを製品ループと metric 評価へ繋ぎ、Exit を取り直す

## 目的

Phase 5 Exit の未成立4条件を、合成テストの外側で閉じる。recorded と live が同じ Observe 経路を通り、frozen metric で Known／Unknown／false-positive を測り、backend 変更・resize・stale と UniqueMatch 以外では dispatch しない。一つの実 game 成功を一般対応と表示しない（先行 t10 を壊さない）。

## 統括レーン判定と F/A/H

統括レーン成立根拠: ②受入が多段連鎖（Frame→Observation→gate→dispatch／metric）④裁定証跡が必要。①の Exit オーナー承認待ちは組まない。

- **F（統括直轄）**: 事前固定の合格基準、commit・push、計画正本、Phase gate。t06（再 Exit）は親が宣言して閉じる
- **A（委譲可の実装物量）**: fixture recognizer、recorded／live conformance、metric runner、ContinuityGate／LiveResumeGate の製品 dispatch 配線、自前 window の Windows native
- **H**: 実 game 窓（NIKKE 本体）の live、UAC、実機残置。自前 window の WGC は H ではない

### 事前固定の合格基準（F・評価前に固定）

acceptance を見て閾値を動かさない。acceptance は training API に載せない。

- Known 誤判定: ラベルが Known でない item を Known にした件は 0
- Unknown 棄却: ラベル Unknown の item を Known にした件は 0
- success false-positive: ラベルが UniqueMatch 再開不可の item で `DispatchAllowed=true` になった件は 0
- 実 game／NIKKE の一般対応は claim しない。fixture と自前 window の成立は pilot 面だけ

## 円卓

入口は **peertable room `OpenLogicool`** だけ。既存部屋を継続する（setup.sh／parent-join はしない）。pull run は本 plan 用に席が新規作成する。Phase 5 本体の run を再利用しない。

| 役割 | 配置 | 入口 |
|---|---|---|
| 統括・受入・commit | Grok 4.6（親） | HTTP API。member は bell |
| 実装物量（A） | Terra×high（`gpt-5.6-terra`） | peertable Codex 席。worker は ready＋active 実装 ToDo 数 |
| 監査担当（閉じ） | Claude opus×medium | 同じ円卓の別席。実装と監査は別ベンダー |
| 契約クリティカル反証 | Grok 4.6×high | 親直轄。円卓外 read-only の `spawn_subagent` refuter は可 |

Grok の `spawn_subagent` は円卓ではない。

## 非目標

- Phase 6 の AI Teach／NextActionProposal
- 実 NIKKE を一般対応にする
- OCR／装飾／実画像の磨き
- Desktop Duplication／GDI の製品化（先行 t03 の非採用を覆さない）
- fast path へ capture／Perception を載せる
- 失敗した backend への黙った fallback
- 先行 t01〜t10 のライブラリ契約を作り直す

## 既知の罠

- WGC は変化駆動。静止の無 frame は失敗ではない
- capture／Perception は Device Input→Emitter を待たせない
- Perception は Attempt を知らない
- FakeObservationSource が frame を捨てる経路を recorded の証明に使わない
- ContinuityGate の bool だけでは入力は止まらない。dispatch が読め
- 製品 `IFrameRecognizer` が無いと実画面 UniqueMatch は出せない
- in-progress の start actor と席名が一致しないと `TASK_START_BINDING_UNSUPPORTED`
- Codex PATH の pwsh は `C:\Program Files\PowerShell\7\pwsh.exe`

## 受入条件

1. recorded fixture と live WGC frame が同じ `LiveObservationSource.Observe` を通り、Observation conformance を満たす
2. 上記の事前固定基準で metric 評価が走り、結果が証跡に残る
3. backend change／resize／stale で製品 dispatch が止まる（静止無 frame では止まらない）
4. UniqueMatch 以外、および window／capture／input 不一致では resume dispatch しない
5. 一つの実 game 成功を一般対応と表示しない（先行 t10 を維持）
6. 各 ToDo は focused test green＋証跡で閉じ、対象限定 commit・push 済み
7. `docs/phase5-exit-assessment.md` を取り直し、未成立は未成立と明記する
8. 通し試験は Exit の最終確認だけ

## 検証方法

- Frame／Observation: recorded 画素 → CapturedFrame と live WGC → CapturedFrame を同一 Observe へ
- metric: frozen acceptance を runner が集計。training 型に acceptance が無いことを維持
- dispatch: ContinuityGate と LiveResumeGate を製品経路が参照する focused test と、自前 window の Windows native
- 通し試験は t06 で1回

## 運用

- 各 ToDo は仕様固定（F）→実装（A）→監査担当クローズ→intake accept→着地（F）
- 次工程は監査担当の「次の工程に着手してください」だけ。具体工程は指示しない
- 別問題は Lattice note。完了条件へ無断追加しない
- 技術判定をオーナーへぶん投げない
- t06 は親手番。席は取らない

## Lattice task 仕様（正本は store。以下は起票時の作業指定）

### t01-recorded-live-conformance

recorded 画素（既存 fixture の PNG／bytes）を `CapturedFrame` にし、live WGC の自前 window frame も同じ型にする。両方を `LiveObservationSource.Observe` へ渡す。`FakeObservationSource` の queue 差し替えを recorded 証明に使わない。Observation conformance と focused／WindowsNative。t03 の recognizer を使う。

### t02-frozen-metrics

CorpusPartition の acceptance だけを読む評価 runner を置く。training API に acceptance を載せない。ラベル付き fixture で Known 誤判定／Unknown→Known／success false-positive を集計し、本書の事前固定基準で合否を出す。acceptance を見て閾値や recognizer を動かさない。

### t03-fixture-recognizer

製品 `IFrameRecognizer` を置く。対象は本 campaign の fixture／自前 window 状態だけ。未校正・候補なしは Unknown、複数は Ambiguous、契約外は明示エラー。Known へ丸めない。実 game 一般対応を claim しない。

### t04-continuity-dispatch

`CaptureContinuityGate` を製品 dispatch 経路が読む。backend change／resize／stale では dispatch しない。静止の無 frame では止めない。FastPathPump には載せない。InputEmitter を gate 拒否のあとに呼ばない。focused test。

### t05-unique-resume-loop

同じ製品 dispatch 経路が `LiveResumeGate` を読む。UniqueMatch 以外、鮮度超過、安定窓未達、window／capture source／input target 不一致では dispatch しない。自前 window の Windows native で許可1と拒否を実測する。実 NIKKE は H のまま未確認でよい。

### t06-phase5-exit-reassess

full regression 1回、Grok read-only 監査、`docs/phase5-exit-assessment.md` を Exit 条件×4値で取り直す。技術成立したら親が Exit を宣言して閉じる。オーナー承認待ちで止めない。席は本 task を取らない。
