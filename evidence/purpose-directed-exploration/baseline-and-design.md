# 目的指向の逐次探索 baseline／design

判定日: 2026-08-25

## Baseline

- HEADと`origin/main`は`8f86f918e7222c12f7cd68e37e437bf9fadf1892`で一致した。
- 既存dirtyは別作業所有のProbe 3差分と未追跡probe出力であり、内容を読まず保護対象とした。
- `OpenLogicool.Host.Tests`: 196件green。
- `OpenLogicool.Playbooks.Tests`: 154件green。
- `OpenLogicool.Persistence.Tests`: 50件green。

## Discovery

- `ProductGameExplorerRuntime`は基本10機能を一手へ合成し、`Moved`／`Stayed`／`Undetermined`を学習できる。
- `WindowsKnownFirstTargetDiscovery`は保存actionをAIより先に使い、10秒非遷移後の再探索を起動できる。
- `SqliteLearningRouteStore`はappend-only revisionを保存できる。
- 現行には、利用者goalを複数stepで進め、`Moved` edgeをrouteへ逐次追記し、restart後に同じrouteをAI 0で再生する上位runtimeがない。

## Design decision

既存一手runtimeを変更対象の下位正本として再利用し、goal、route cursor、逐次保存、失敗stepの差替えだけを新しい上位runtimeが所有する。Compare中の観測では次step用AIを呼ばず、保存route再生ではStructure edgeのtarget semantic keyとprimitiveをknown-first discoveryへhintとして渡す。

詳細は`docs/purpose-directed-exploration-campaign-plan.md`を正とする。
