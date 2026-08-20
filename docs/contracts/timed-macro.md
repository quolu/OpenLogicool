# Timed macro 契約

## 範囲

`TimedMacro` は delay、repeat while held、toggle、有限回 repeat を pure state machine として表す。timer、thread、SendInput、device write は持たない。

## 停止境界

- emission は一回ごとに完結する tap action であり、保持 output を作らない。
- `Stop()` 後は `Resume()` 前の `AdvanceTo()` が新しい action を返さない。
- scheduler 遅延後に過去の due 時刻を catch-up して連射しない。

## profile 適用

timed macro binding は control/layer cell ごとに通常 output binding と排他的である。`ValidateForProfileApplication` は混在、重複、存在しない layer を拒否する。

既存の有限 `Tap:` sequence は DEV-006 の実装のままとし、timed macro 定義には混在させない。
