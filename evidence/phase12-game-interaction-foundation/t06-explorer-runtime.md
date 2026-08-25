# t06 製品Explorer runtime

## 結論

基本10機能を、WGC→認識→候補選択→Game Policy／risk→owner delegated一手承認→Nano一回dispatch→安定待機→意味判定→Transition Evidence→candidate Screen Graphの一本道へ合成した。`IHostExplorerRuntimeControl`には実装が入り、Probe専用runnerなしで製品runtimeを駆動できる。

## 製品runtime

`ProductGameExplorerRuntime`は`IGameInteractionRuntime`と`IHostExplorerRuntimeControl`を実装する。

- Observe／DiscoverTargets／Hover／Click／KeyTap／Scroll／Drag／WaitStable／Compare／LearnTransitionを公開する。
- `ExecuteNextAsync`は未調査の安全候補を上から順に一つ選ぶ。
- 同じscene semantic signature＋target semantic keyは二度probeしない。
- owner delegated承認は`ExplorationAuthorizationSource.OwnerDelegatedAutomation`としてUser actorへ偽装せず保存する。
- pause／step／abandonを既存Explorer UI portへ提供する。

## risk境界

`DeterministicExplorationCandidateRiskPolicy`が購入、課金、有償、ダイヤ、ジュエル、募集／ガチャ、削除、引退、account変更をProhibitedにする。Prohibited候補はproposalも入力も0。その他の未知候補はElevated＋一手承認とし、side-effect-free／reversibleを勝手にtrueにしない。

## Structure学習

`GameInteractionStructureLearner`が意味構造SHA-256をscene signatureとしてcandidate nodeをdedupeし、Transition Evidenceからcandidate edgeを作る。

- `NoChange`はself-loop。
- `Novel`／`Destination`は異なる意味構造nodeへのedge。
- `OutcomeUnknown`はsource nodeとoutcome evidenceだけを残し、架空destination edgeを作らない。
- edgeはtarget semantic key、risk tag、reversible、wait、before／after、outcomeを保持する。

## Windows正規composition

`WindowsProductGameExplorerComposition`が次を接続する。

- `WindowsWgcGameFrameSource`
- `WindowsGameOcrRecognizer`
- `FoundryLocalControlDiscoveryProvider`
- `SerialHidNanoGameInputDevice`
- `ExplorationCoordinator`／Durable Attempt
- `GameTransitionLearningController`
- `StructureKnowledgeController`
- `GamePolicyGate(Explore)`

## focused検証

- Hostの基盤runtime 4 test class: 13件green
  - 一step全layer一巡
  - owner delegated actor
  - 同じtargetの二重probe 0
  - prohibited候補のproposal／input 0
  - Game Policy拒否時input 0
  - Nano fault再試行0、after observation 0
- `OpenLogicool.Exploration.Tests`全29件green
  - candidate node 2、edge 1
  - semantic同一なら同じnode
  - risk／target semantic key／evidence保持
- Host Windows composition build成功。
- 変更対象の`git diff --check`通過。

## 未検証

Windows正規compositionを実NIKKE＋実Nano＋実Foundry Local＋実SQLiteで起動する証拠は`t07-basic-live`で取得する。
