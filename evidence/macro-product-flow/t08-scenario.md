# t08 fake・SQLite一貫scenario

同じpublic intent／SQLite経路で次を一巡した。

1. 利用者goalから前半macro／後半macroを作成。
2. 前半をAI監視なしで再生し、AI call 0・revision追加0。
3. 前半をAI監視ありで再生し、失敗stepだけ`edge-1`→`edge-1b`へ更新。後続`edge-2`と旧revisionを保持。
4. 修復後の前半と後半を順序付きで統合し、`edge-1b, edge-2, edge-3`の新routeを保存。source routeは変更しない。
5. 統合macroを一つのSemantic ActionとしてG13 G1／G600 G9の両方へ既存Workspace経路で割り当て。
6. DBを閉じて再openし、macro catalog、route history、統合順序、Workspace revision、最新版追従token、両device bindingを復元。

focused test: `MacroProductFlowScenarioTests` 1件 green（2026-08-26）。
