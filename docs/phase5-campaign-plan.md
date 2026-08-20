# Phase 5 campaign — Capture／Perception

- status: **closed**（2026-08-20 初回 Exit は未成立。同日 companion `phase5-perception-close` で取り直し、親が成立を宣言。[phase5-exit-assessment.md](phase5-exit-assessment.md)）
- 起票: 2026-08-20（オーナー指示「Lattice を使って円卓を立てろ」）
- 統括: ベル（本セッション親は Grok 4.6）。2026-08-20 円卓再立：実装 Terra×high（Codex）／監査 Grok 4.6×medium。Phase 4 の sonnet/fable 表は使わない
- 実行 TODO の正本: **Lattice plan `phase5-capture-perception`**（本書は目的・思想・非目標・受入条件だけを所有し、ToDo を二重化しない）
- 上位正本: [development-plan.md](development-plan.md) §Phase 5 および §6.9、CAP／PER／KP-001〜004
- 先行: Phase 4 Exit 成立（[phase4-exit-assessment.md](phase4-exit-assessment.md)）。resume の判定は Phase 4。本 campaign は実画面 Observation を供給して UniqueMatch 再開を閉じる

## 目的

実画面を契約済み Observation へ変換する。recorded と live が同じ Frame／Observation 契約を満たし、backend 変更・resize・stale では入力を止める。一つの実 game 成功を一般対応と表示しない。Phase 4 と繋ぎ、実画面 UniqueMatch のときだけ resume する。

## 統括レーン判定と F/A/H

統括レーン成立根拠: ②受入が多段連鎖（Frame→fault→Observation→resume／corpus）④裁定証跡が必要（conformance・metric・live／recorded）。①の Exit オーナー承認待ちは組まない。

- **F（統括直轄）**: Contracts の Frame／Observation 拡張、capability matrix からの backend 採否、commit・push、計画正本、Phase gate。各 ToDo のクローズは監査担当。t11（Exit）は親が宣言して閉じる
- **A（委譲可の実装物量）**: Capture／Perception／Knowledge Pack／corpus／failure UX／UniqueMatch 配線＋focused test
- **H**: credential・publish・本番・意図的障害、および人が機械を動かさないと取れない観測（実機ウィンドウの用意・UAC）だけ。Phase Exit で止めない。WGC 以外を製品化するかは capability matrix を材料に親が決めて進める（§16。第一経路は WGC）

## 円卓

入口は **peertable room `OpenLogicool`** だけ。既存部屋を継続する（setup.sh／parent-join はしない）。pull run は本 plan 用に席が新規作成する。Phase 4 の run を再利用しない。

| 役割 | 配置 | 入口 |
|---|---|---|
| 統括・受入・commit | Grok 4.6（親） | HTTP API。member は bell |
| 実装物量（A） | Terra×high（`gpt-5.6-terra`） | peertable Codex 席。worker は ready＋active 実装 ToDo 数 |
| 監査担当（閉じ） | Grok 4.6×medium | 同じ円卓の別席。実装と監査は別ベンダー |
| 契約クリティカル反証 | Grok 4.6×high | 親直轄。円卓外 read-only の `spawn_subagent` refuter は可。実装成果が Grok のときは反証を別ベンダーにする |

Grok の `spawn_subagent` は円卓ではない。

## 非目標

- AI Teach／NextActionProposal の製品接続（Phase 6）
- Observe Only の Screen Graph candidate 蓄積の本運用（KP-005 は Phase 5／6。本 campaign は schema と corpus まで）
- 装飾・実画像の磨き
- UI 保存と関連付けの導線統合（Phase 3 持ち越し）
- G13 LCD／G600 RGB 等の残置製品化
- 失敗した backend への黙った fallback

## 既知の罠

- WGC の frame 供給は変化駆動。静止は失敗ではない（[probes/wgc-frame-supply-2026-08-15.md](probes/wgc-frame-supply-2026-08-15.md)）
- 最小化は item 有効・frame 停止＋サイズ急変。静止とは size で区別する
- capture 失敗を別 backend へ黙って切り替えない（CAP-004）
- fast path 純潔: capture／Perception は Device Input→Emitter を待たせない
- Perception は Attempt を知らない。Confirmed 束縛は Playbook の RunEvent
- Codex Windows sandbox は 2026-08-20 に復旧（Store/WindowsApps の pwsh エイリアスが原因。PATH は MSI の `C:\Program Files\PowerShell\7\pwsh.exe`）
- 円卓は peertable。`spawn_subagent` implementer を円卓と読まない
- in-progress の start actor と席名が一致しないと pull run intake が `TASK_START_BINDING_UNSUPPORTED` になる。散会後の再開は同名再着席か、元 actor の retract だけ

## 受入条件

1. development-plan §Phase 5 Exit が `docs/phase5-exit-assessment.md` に4値で揃っている
2. recorded／live frame が同じ Frame／Observation conformance を満たす
3. Known 誤判定、Unknown 棄却、success false-positive を事前固定 metric で評価している
4. backend change、resize、stale frame で入力を止める
5. 一つの実 game 成功を一般対応 claim にしていない
6. 実画面 UniqueMatch のときだけ resume する
7. 各 ToDo は focused test green＋証跡で閉じ、対象限定 commit・push 済み
8. 未成立は未成立と明記する

## 検証方法

- Frame／Observation: recorded fixture と live の同一 conformance
- fault: 最小化・stale・backend 切替で入力停止を focused test
- 通し試験は Exit の最終確認だけ。親が宣言して閉じる

## 運用

- 各 ToDo は仕様固定（F）→実装（A）→監査担当クローズ→intake accept→着地（F）
- 次工程は監査担当の「次の工程に着手してください」だけ。具体工程は指示しない
- 別問題は Lattice note。完了条件へ無断追加しない
- 技術判定をオーナーへぶん投げない

## Lattice task 仕様（正本は store。以下は起票時の作業指定）

### t01-wgc-frame

CAP-001。WGC window を第一 backend として製品 Frame を供給する。sequence、monotonic time、size、pixel format、color space、DPI、rotation、crop 変換。Phase 0 probe の確認済み経路を製品モジュールへ。fallback しない。focused test。

### t02-capability-matrix

CAP-004／005。windowed／borderless／fullscreen、DPI、HDR、multi-monitor、遮蔽、最小化の support matrix。backend 選択と失敗理由を記録する口。WGC 以外を製品化するかは matrix を材料に親が決める。未確認を Supported と書かない。

### t03-alt-backends

Desktop Duplication と可視領域を別 backend として明示選択可能にする（CAP-004）。切替は利用者へ明示。失敗を他 backend へ黙って落とさない。t02 の採否に従う。不採用なら非対応と表示して実装しない。

### t04-frame-transform

resize、display 移動、DPI、HDR／format、letterbox。transform revision を更新し古い locator を無効化（§6.9）。座標系は source→content→normalized→client→input。

### t05-capture-faults

CAP-002／003。black、stale、drop、resize、device lost、backend change、遮蔽、最小化を別状態。backend 変更・座標系変更・stale では観測連続性を切り、再校正まで自動入力を止める。静止による無 frame を失敗にしない。

### t06-live-observation

PER-001〜004。recorded／live frame から Observation（Known／Ambiguous／Unknown／Unavailable）。frame ID、age、recognizer version、候補、校正済み confidence、evidence region。Known 以外を自動実行条件にしない。成功は安定窓。Perception は Attempt を知らない。

### t07-knowledge-pack

KP-001〜004。Knowledge Pack schema（game/build、locale、UI scale、state、anchor、success condition、action 参照、schema、出典、license、検証状態）。実行 code／任意 script／秘密を含めない。import 直後は Untrusted／Candidate。Screen Graph は独立成果物として version できる。実装は schema と検証まで。

### t08-corpus-split

development／calibration／acceptance corpus を分離。NIKKE 等の探索 frame を再現可能な experiment artifact にする。acceptance を prompt 調整や Recognizer の過学習に使わない口を構造で持つ。

### t09-unique-resume

Phase 4 の ResumeGate に実画面 Observation を供給する。UniqueMatch 以外は自動再開しない。PER-005: 対象 window、capture source、input target 不一致は dispatch 前に停止。

### t10-failure-ux

capture／認識失敗を利用者へ明示。一つの実 game 成功を一般対応と表示しない。絶対座標だけの step は fragile と表示（PER-006）。

### t11-phase5-exit

full regression 1回、Grok read-only 監査、`docs/phase5-exit-assessment.md` を Exit 条件×4値で作成。技術成立したら親が Exit を宣言して閉じる。オーナー承認待ちで止めない。
