# Phase 6 campaign — AI Teach／Learn

> Historical plan: 本書の一手承認口はPhase 6当時の受入であり、2026-08-25以降の通常操作gateではない。現行裁定は`development-plan.md` §0.3を正とする。

- status: **completed**（2026-08-20 Exit 宣言・[phase6-exit-assessment.md](phase6-exit-assessment.md)）
- 起票: 2026-08-20（オーナー指示。Phase 5 Exit 成立後）
- 統括: ベル（Grok 4.6）。実装 Terra×high（Codex）／監査 Grok 4.6×medium
- 実行 TODO の正本: **Lattice plan `phase6-ai-teach`**
- 上位正本: [development-plan.md](development-plan.md) §Phase 6、§6.10、AI-002、EXP-AI-01
- 先行: Phase 5 Exit 成立。provider は未選定のまま（§16。Teach 実装前に EXP-AI-01）

## 目的

利用者が goal を出し、AI が NextActionProposal だけを返す。AI は input／DB／device に届かない。未知 step は candidate として育ち、GameLab の Verified は実 game へ継承されない。

## 統括レーン判定と F/A/H

②多段受入 ④証跡。① Exit オーナー待ちは組まない。

- **F**: schema 契約、provider 未選定の維持、commit・push、t08 Exit
- **A**: schema 実装、隔離 test、拒否口、eval harness、Observe Only／Teach の製品口
- **H**: 有料 provider の本番課金、cloud へ実 game 画像を送る consent。GameLab 合成は H ではない

## 円卓

入口は peertable room `OpenLogicool`。setup.sh／parent-join はしない。pull run は本 plan 用に新規。`phase5-unverified` の ready が残っている間はそちらを先に取る。

| 役割 | 配置 |
|---|---|
| 統括 | Grok 4.6（bell） |
| 実装 | Terra×high Codex |
| 監査 | Grok 4.6×medium |

待機中は `[次の行動]` 自己DMを出さない。

## 非目標

- provider を仮決めして実装する
- AI が SendInput／HID／SQLite を直接呼ぶ
- 実 game の Verified 昇格
- acceptance を prompt 調整に使う
- 装飾 UI

## 受入条件（§Phase 6 Exit）

1. AI が direct input／DB／device API へ到達できない（dependency test）
2. schema 外、catalog 外、state 不一致、risk 不一致 proposal を dispatch 前に拒否
3. 初見 GameLab scenario を途中保存し、別 session で既知を replay、未知だけ追記できる口がある
4. GameLab の Verified が実 game へ継承されない
5. acceptance dataset を prompt 調整へ使っていない（構造）
6. provider 停止時も Input Studio と verified deterministic Playbook が使える
7. EXP-AI-01 は比較 harness まで。provider を選定しない
8. 各 ToDo focused green＋証跡＋着地。通し試験は Exit だけ

## Lattice task 仕様（正本は store）

### t01-planner-proposal-schema

`PlannerContext` と `NextActionProposal` の契約を製品 schema として閉じる。goal、allowed action、budget、precondition、expected outcome、stop。未知 schema version は拒否。focused conformance。

### t02-ai-isolation

AI プロジェクトが Input／Devices／Persistence／Capture を参照しない。proposal 以外を返さない。architecture／dependency test。AI-002。

### t03-proposal-reject

schema 外、catalog 外、state 不一致、risk 不一致の proposal を dispatch 前に拒否する。InputEmitter を呼ばない。focused。

### t04-exp-ai-01-harness

frozen corpus で精度、unknown 棄却、latency、cost、cancel を測る口。provider を選定しない。Phase 5 corpus を使う。acceptance を prompt 調整 API に渡せない。

### t05-observe-only

Observe Only mode。proposal を出しても dispatch しない。Playbook を書き換えない。focused。

### t06-teach-supervised

Teach／Supervised の一手承認口。承認前に SendInput しない。provider 未選定でも schema と fake provider で口だけ閉じる。本番 provider を埋め込まない。

### t07-verified-env-scope

GameLab Verified の environment scope。実 game へ継承しない。focused。

### t08-phase6-exit

full regression 1回、Grok read-only 監査、`docs/phase6-exit-assessment.md`。親が宣言。席は取らない。provider 未選定を維持して閉じる。
