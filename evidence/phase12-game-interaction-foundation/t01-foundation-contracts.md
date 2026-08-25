# t01 基本10機能の契約固定

## 結論

基本10機能を`IGameInteractionRuntime`の公開portとして固定した。既存Phase 9の部品は再利用するが、Probe固有スクリプトを製品runtime成立の証拠には数えない。

## 実装対応監査

- `Observe`: WGCと`ObservationResult`は存在するが、製品runtime compositionは未実装。
- `DiscoverTargets`: Foundry Local、OCR、`AffordanceCandidate`は存在するが、Host接続は未実装。
- `Hover`／`Click`／`KeyTap`／`Scroll`: Nano部品は存在するが共通portは未実装。
- `Drag`: Nano部品の合成も製品portも未実装。
- `WaitStable`／`Compare`: contract上のstable countとoutcome列挙だけで、意味的な実画面判定器は未実装。
- `LearnTransition`: Structure Event Storeと`RecordOutcome`は存在するが、実画面判定を供給するruntimeが未実装。
- `IHostExplorerRuntimeControl`: interfaceだけで実装は0件。
- 既存live click probeは必須label／pinned observation依存で、画面SHA差分を結果判定に使っている。

## 変更

- `src/OpenLogicool.Contracts/Exploration/GameInteractionContracts.cs`
  - 基本10機能名
  - Observation固定target
  - Nano dispatch receipt
  - 安定窓
  - `Moved`／`Stayed`／`Undetermined`
  - 学習要求
  - `IGameInteractionRuntime`
- `tests/OpenLogicool.Conformance.Tests/GameInteractionContractTests.cs`
  - 10機能の全量／一意性
  - Observation／frame／transform／window／locator束縛
  - dispatchとゲーム結果の分離
  - timeoutを`Stayed`へ丸めない契約
- `docs/game-interaction-foundation-contract.md`
  - 既存部品との対応表と禁止する成功判定

## 検証

`dotnet test tests/OpenLogicool.Conformance.Tests/OpenLogicool.Conformance.Tests.csproj --no-restore --filter FullyQualifiedName~GameInteractionContractTests`

- 4件green
- failure 0
- skip 0
- `git diff --check`通過

## 未検証

このToDoは契約固定だけを受け入れる。製品runtime、Nano実入力、意味的遷移判定、NIKKE実証は後続ToDoで確認する。
