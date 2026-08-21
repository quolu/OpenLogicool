# Input Studio isolation contract

`InputStudioIsolation` は、Game Operator の AI・network・capture dependency が fault した場合にも、Input Studio の既存操作を停止しないための Host 側の pure contract である。

## 保持する操作

- mapping の編集
- profile の保存
- mapping の実行

依存 fault が一つでもある時、`IsGameOperatorDegraded` は `true` となり、fault した dependency は `FailedDependencies` に明示する。Game Operator が利用可能だという fallback claim はしない。

## 境界

この契約は障害を分類するだけで、AI・network・capture の呼び出し、dispatch、設定保存、watchdog、fast path を開始・停止・再実装しない。したがって外部依存の失敗は Input Studio の既存操作面へ伝播しない。
