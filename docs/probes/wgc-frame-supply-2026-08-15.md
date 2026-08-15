# WGC の frame 供給は変化駆動である（Deliverable 0B 追試 / 2026-08-15）

capture probe を GameLab prototype へ適用する過程で発見した、**製品設計に直接効く capture API の性質**。根拠水準: **確認済み**（対照実験で切り分け済み）。

## 事実

**Windows Graphics Capture の `Direct3D11CaptureFramePool` は、対象 window が再描画されたときにだけ frame を供給する。静止した window では2枚目以降の frame が来ない。**

## 対照実験（すべて同一 probe・同一マシン・同一日）

| 対象 | 内容変化 | frame0 | frame1 | 一次データ |
|---|---|---|---|---|
| GameLab prototype（フォーカスなし・完全静止） | なし | 81ms で取得 | **5000ms timeout** | `capture-wgc-window-20260815-161541.json` |
| 同上（window を 100ms 間隔で移動） | 位置のみ・内容不変 | 78ms で取得 | **5000ms timeout** | `capture-wgc-window-20260815-161646.json` |
| 同上（200ms 間隔でキー送信＝再描画発生） | **あり** | 17ms で取得 | **89ms で取得** | `capture-wgc-window-20260815-161756.json` |
| メモ帳（空・フォーカスなし） | なし | 84ms で取得 | **5000ms timeout** | `capture-wgc-window-20260815-161722.json` |
| メモ帳（テキストあり・フォーカスあり＝カーソル点滅） | あり | 28ms で取得 | 3341ms で取得 | `capture-wgc-window-20260815-151215.json` |
| モニタ全体（デスクトップ＝時計等が常に変化） | あり | 220ms で取得 | 382ms で取得 | `capture-wgc-monitor-20260815-151205.json` |

**window の移動では frame が来ない**ことが、「変化＝内容の再描画」であって「見た目の位置変化」ではないことを示している。probe 実装のバグでないことは、同じ probe が変化ありの対象では即座に2枚目を返すことで切り分け済み。

## 製品設計への含意（重要）

1. **「frame が来ない」を capture 失敗として扱ってはならない。** CAP-002 の状態分類で `stale` / `drop` / `device lost` と、**正常な静止**を区別する必要がある。静止画面のゲーム（メニュー画面で待機中など）で自動化が誤って停止する。
2. **ObservationResult の `freshnessMs`（[contracts/observation-result.md](../contracts/observation-result.md)）の意味を定義し直す必要がある。** 「最後に取得した frame の経過時間」は、静止画面では際限なく増える。「画面が変わっていないから frame が来ない」状態を、鮮度低下と区別できる表現が要る。
   - 案: `freshnessMs` に加えて `lastChangeMs`（最後に再描画があってからの経過）を持たせ、`Unavailable` 判定は両者の組で行う。**Phase 1 の contract 確定までに決める**。
3. 前 frame と後続 frame の「安定窓」で成功判定する設計（§6.9）は、**変化駆動と相性が良い**: 遷移後に frame が数枚来て、その後止まれば「安定した」と読める。逆に言えば「一定 fps で来ること」を前提にした実装をしてはならない。
4. 最小化 window の失敗系（[capture-backend-matrix-2026-08-15.md](capture-backend-matrix-2026-08-15.md)）は本件と**別現象**である。最小化は `Item.Size` が急変し frame も来ないが、静止は size 不変で frame だけ来ない。この2つは size 変化の有無で区別できる。

## Desktop Duplication について

DXGI Desktop Duplication の `AcquireNextFrame` も変化がなければ待つ API であり、同じ性質を持つと**強い推定**。本日の `dup` 実測で frame1 が 387ms で来たのは、対象がデスクトップ全体で常に何かが変化していたためと説明できる。**静止デスクトップでの dup 対照実験は未実施**（画面を完全静止させる必要があり、この環境では取りにくい）。

## probe への反映

現時点では probe を変更しない。「2 frame 取得を試み、来なければ timeout を記録する」という現在の挙動は、**この性質を可視化する道具として正しく機能した**（timeout が出たことで発見できた）。製品実装では上記1〜3の設計判断が要る。
