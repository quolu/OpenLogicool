# GameLab prototype 仕様（Phase 0 / 2026-08-15）

G0-Automation gate「Frame・Observation・Proposal の draft が**実 frame と GameLab prototype の fixture 案**で表現可能」を成立させるための prototype 仕様。GameLab v1 本体（scenario oracle・fault hooks・virtual clock の完全形、計画 §Phase 1）の前身であり、**v1 の実装受入はここに含めない**。

## 目的（この prototype が証明すること）

1. 決定的（seed 固定で同じ画面列）に動く模擬ゲーム画面を WPF で作れる
2. その画面を capture probe（WGC window capture、実測済み）で実 frame として取得できる
3. その実 frame に対する ObservationResult / NextActionProposal の fixture が draft contract で書ける
4. 画面とは独立の ground-truth oracle（真の状態記録）を持てる——capture・認識の正誤を答え合わせできる

## 状態機械（6 state・最小）

```
main-menu ──(OpenEvent)──▶ event-popup ──(ClosePopup)──▶ main-menu
main-menu ──(OpenRewards)─▶ reward-list ──(SelectReward)─▶ claim-confirm
claim-confirm ──(Confirm)──▶ claim-done   ※不可逆: claim-done から戻る遷移は存在しない
claim-confirm ──(Cancel)───▶ reward-list
任意state ──(seed依存の確率遷移)──▶ unknown-glitch（回復ボタンなし。手動介入のみ）
```

- **不可逆 claim**: `claim-done` への遷移で報酬 ID を消費し、同一 seed 内で再 claim できない（irreversible claim の模擬）
- **popup**: `event-popup` は起動直後に seed 依存で自動表示されることがある（初回起動 popup の模擬）
- **unknown state**: `unknown-glitch` は既知遷移の外にある状態。自動化は停止し、手動介入だけが復帰経路
- **daily reset / delay / fault hooks は v1 へ持ち越し**（本 prototype の範囲外。仕様上の席だけ確保）

## 決定性

- `--seed <int>`: 全確率遷移（glitch 発生・初回 popup）は seed 固定の擬似乱数だけから決まる。同じ seed ＋同じ操作列＝同じ状態列
- wall clock を状態遷移の入力にしない（v1 の virtual clock の席を汚さない）

## Oracle（ground truth）

- 全状態遷移で 1 行 JSON を append: `{seq, monotonicMs, stateId, cause}` を `gamelab-oracle-<seed>-<起動時刻>.jsonl` へ書く
- `cause` は `button:<name>` / `auto:seed` / `manual-intervention` のいずれか
- **stateId は Knowledge Pack `states` の stable state ID と同じ語彙**を使う（`state.main-menu` 等）。契約 fixture と oracle が同じ ID で突合できることが要点

## 画面（認識容易性を優先）

- state ごとに背景色を変え、state ID を大きな文字で中央表示（recognizer の fixture を単純にするため）
- 遷移ボタンはウィンドウ下部に固定配置。ボタン名＝遷移名
- キーボード入力はすべて `manual-intervention` として oracle に記録（自動化外の介入の模擬）

## 受入（Phase 0 のこの prototype に対するもの）

1. seed 固定で同じ操作列が同じ oracle 出力を生む（focused test で2回実行比較）
2. WGC window capture で本 prototype の実 frame（非黒・state 文字が写る）が取得できる
3. その実 frame を参照する ObservationResult fixture と、それに対する NextActionProposal fixture が draft contract の語彙だけで書ける
4. `claim-done` からの逆遷移が存在しないことが oracle で示せる

## 配置

- `src/OpenLogicool.GameLab.Prototype/`（net10.0-windows、WPF、依存なし）
- v1 実装時は `OpenLogicool.GameLab/`（計画 §5 の席）へ置き直し、本 prototype は fixture 生成器として残すか破棄するかを Phase 1 で決める
