# t08 Exploration Coordinator 実装・検証記録

取得日時: 2026-08-24
対象: Phase 9A / `t08-exploration-coordinator`
状態: **成立**

## 結論

観測commit、AI proposal、deterministic policy／一手承認、Durable Attempt、再観測、Transition Evidence、StructureDelta検証を`OpenLogicool.Exploration`へ配線した。AI／Perception／ExplorationからInput、device、SQLite、capture実装へ直接到達する経路はない。

- ObservationはStructure Event StoreとRun Journalへ別々にcommitし、同じRun内の現在sceneとして固定する。
- `ExplorationProposal`はschema、immutable policy／consent、source revision、Game Policy、探索scope、capture可否、freshness、Observation／Frame／transform、target window、normalized locator、primitive、deterministic risk、budget、既知復帰edgeをdispatch前に検証する。
- proposalが持つContextはcommit済みsceneのcapture状態、frame、候補locator、primitiveと再照合し、同じObservation IDを名乗る候補差替えを拒否する。
- 初回または非自動条件の一手承認はObservation、proposal、policy revision、Structure revisionへ束縛する。不一致承認はAttemptを作らない。
- PlaybooksのAttemptとRun Journal上の`DispatchArmed`をcommitし、Structure Store上の`DispatchArmed`をappendした後だけ外部入力delegateを一度呼ぶ。入力例外を自動再送しない。
- Structure appendが入力前に失敗した場合だけdurable `Disarmed`を記録する。入力を呼び得た境界以降の未解決Attemptはrestart後に`OutcomeUnknown`となる。
- 安定再観測が成立した`Destination`／`Novel`だけをConfirmed、`NoChange`をRejectedへ進める。Ambiguous／Unavailable／Fault／OutcomeUnknownは確定成功へ丸めない。
- `Rejected`も同じ再観測Observation IDへ束縛してjournal replayできる。
- 同一probe反復、連続no-progress、ABAB振動、capture喪失、stale frame、budget、復帰edge喪失を停止理由として保持し、停止後の新規Attemptを作らない。

## Structure Knowledge Controller

`StructureDeltaProposal`はStructure Eventではない。`StructureKnowledgeController`だけが次を検証し、受理proposalとmutation batchを新revisionへappendする。

- source Structure revisionと現在revisionの一致
- 参照evidenceがStructure Event Storeに存在すること
- proposal operationとmaterialized mutationの一対一対応
- node／edge／factのstable IDがcontroller発行であること
- environment scopeとCandidate状態
- create、edge帰属、fact抽出、merge／split、relabel、retireのoperation種別対応
- AI deltaからReplayed／Verifiedへ昇格しないこと

AIはstore、SQLite、Inputへ直接依存せず、proposal dataだけを返す。

## focused test

- `OpenLogicool.Exploration.Tests`: 15件 green
- `OpenLogicool.Playbooks.Tests`: 143件 green
- `OpenLogicool.Domain.Tests`: 101件 green
- `OpenLogicool.Conformance.Tests`: 57件 green
- `OpenLogicool.Architecture.Tests`: 8件 green
- `dotnet build OpenLogicool.sln --no-restore --nologo`: 成功
- Exploration製品project内のInput／device／Persistence／Capture実装、SendInput、Nano、SQLite、HTTP、API key参照: 0件

focused testは、一手承認の束縛とdurable順序、成功／NoChangeの再観測終端、restart replay、外部入力例外のno-retry／OutcomeUnknown、stale／scope／risk／budget／復帰喪失、拒否後の次proposal、停止持続、Observation／locator差替え、window外target、反復no-progress、安定窓不足、既存evidenceとcontroller発行ID、AI verification昇格拒否を確認した。

## 次工程との境界

t08は探索loopのauthorityと永続境界を所有する。hidden-oracle上でnode 3件、edge 2件、loop／no-change、crash、capture loss、stale、budget、recovery loss、restartを一巡させる受入はt10、GameLab runtimeへの実配線はt11が所有する。
