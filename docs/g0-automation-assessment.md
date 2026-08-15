# G0-Automation gate 判定材料（Phase 0 / 2026-08-15）

計画の Exit Gate G0-Automation 3条件に対する現在地。**gate 通過の裁定はオーナー領分**であり、本書は判定材料の提示に留める。

## 条件1: Frame・Observation・Proposal の draft が実 frame と GameLab prototype の fixture 案で表現可能

**成立（確認済み）。** 実測による連鎖が繋がった:

1. GameLab prototype（[docs/gamelab-prototype-spec.md](gamelab-prototype-spec.md)、seed 固定の決定的状態機械）を起動
2. capture probe の WGC window capture で**実 frame** を取得（706×473、平均輝度 123.3、非黒）
3. 同時刻の **oracle ground truth**（`state.main-menu`）を取得
4. その実 frame と oracle を参照する ObservationResult fixture を draft contract の語彙だけで記述（[fixtures/contracts/observation-result.gamelab-live.json](../fixtures/contracts/observation-result.gamelab-live.json)）
5. それに対する NextActionProposal fixture も draft の語彙で記述可能（[fixtures/contracts/next-action-proposal.sample.json](../fixtures/contracts/next-action-proposal.sample.json) と同形）

**contract の語彙で表現できないものは出なかった。** ただし条件1の検証過程で contract の1項目に修正が必要と判明した（下記「発見」）。

## 条件2: cloud 送信前の Data Flow Contract 項目が決定

**成立（項目決定の範囲で）。** [contracts/data-flow-contract.md](contracts/data-flow-contract.md) に対象 12 data の「生成元・保存先・送信先・retention・削除経路・既定」表を作成した。§6.12 の初期既定（full-screen frame 保存 OFF、cloud 送信は app 単位で明示同意まで OFF、OCR/prompt 本文の log 記録 OFF、crash raw dump OFF、telemetry OFF）を全て反映済み。retention の具体日数など値の確定は Phase 4 期限（§6.12）であり、Phase 0 の Exit 条件である「項目の決定」は満たしている。

## 条件3: NIKKE 実測を探索証拠として再記録し、製品受入と混同していない

**成立。** 計画 §0 が既に「NIKKE の可視 desktop 領域を2回取得できた実測は、探索的な capture 成立証拠に限る」と明記し、継続 capture・WGC window capture・遮蔽・最小化・認識 pipeline・操作結果確認・規約許可を未確認として除外している。本日の capture 実測は GameLab prototype とメモ帳に対して行っており、NIKKE 実測を製品受入の証拠に使っていない。

## 発見: contract の修正が必要（条件1の副産物）

[probes/wgc-frame-supply-2026-08-15.md](probes/wgc-frame-supply-2026-08-15.md) で確認済みのとおり、**WGC の frame 供給は変化駆動**であり、静止画面では frame が来ない。このため ObservationResult の `freshnessMs` 単独では「画面が静止しているだけの正常」と「capture が壊れた異常」を区別できない。

- 対応案: `lastChangeMs`（最後の再描画からの経過）を追加し、`Unavailable` 判定を両者の組で行う。
- **Phase 1 の contract 確定までに決める**。Phase 0 の draft は「未決定」として明記済み。

この発見自体が、G0-Automation を「prototype と仕様で判定する」ことの有効性を示している——実 frame を通したからこそ、机上では見えなかった contract の穴が出た。

## 裁定

- **G0-Automation 通過（オーナー裁定 2026-08-15）**。3条件すべての材料成立を確認のうえ、オーナーが通過と裁定した（「GATEの定義誰がしてるのか知らんけど、そんなもの気にせず進めてほしい」＝判定材料が揃った gate は通過扱いで前進せよ、の指示）。
- これにより **Phase 1: Contract／GameLab Foundation** へ着手可能。device write 禁止（G0-Device-W 未通過）は継続する。
