# t06 Scene contract migration 実装・検証記録

取得日: 2026-08-24
対象: Phase 9A / `t06-scene-contract-migration`
状態: **成立**

## 結論

- `ObservationResult`をschema `0.3.0`へ移し、capture可否とstate同定を別enumにした。
- captureは`Available`／`Unavailable`／`Stale`、state同定は`Known`／`Novel`／`Ambiguous`／`InsufficientEvidence`で表す。
- `Available + Novel`を第一級の結果として保持できる。未知画面をcapture失敗へ丸めない。
- 旧`ObservationStatus`は製品、tool、test、fixtureから全て除去した。
- `ObservedScene`、`AffordanceCandidate`、`ExplorationPolicy`／`Context`、`ExplorationProposal`、`StructureDeltaProposal`、`TransitionEvidence`、`GameStructureRevision`、`GameStateFact`をshared contractへ追加した。
- `ExplorationProposal`と`StructureDeltaProposal`はAI所有namespace、policy／context／evidence／revisionはExploration所有namespace、scene／affordanceはPerception所有namespaceに分けた。
- `AffordanceCandidate`はObservation ID、frame sequence、transform revision、target window、locator、evidence、confidence、許可primitiveを必須fieldとして持つ。

## 同時revision migration

次を同じ変更で`0.3.0`の2軸契約へ移した。

- Perception live／recorded normalization、stability window、frozen metrics
- Domain state matchとresume gate
- Playbooks resume report／live resume
- Host CLI observationとlive dispatch gate
- GameLab status／failure表示／console
- fake observation、recorded fixture、conformance test、capture conformance test

Knownの自動実行条件は`CaptureAvailability.Available`かつ`StateIdentityStatus.Known`の時だけである。Unavailableは`InsufficientEvidence`、Staleは`StaleObservation`、Novelは`InsufficientEvidence`へ写像され、自動再開へ進まない。

## baselineで発見した既存不整合

t06変更前のbaselineで、2026-08-24の外部AI API非依存化により`PlannerBudget.costUsd`が`inferenceMs`へ変更された一方、`fixtures/contracts/next-action-proposal.sample.json`だけが旧fieldを保持し、Conformance 2件が失敗していた。履歴diffで原因を確認し、fixtureを現行wire契約の`inferenceMs: 420`へ移した。製品ロジックの変更やfallbackは行っていない。修正後に同じConformance testがgreenになった。

## focused verification

変更に直結する次のtest projectをWindows nativeで実行し、全てgreenだった。

- `OpenLogicool.Capture.Tests`
- `OpenLogicool.Perception.Tests`
- `OpenLogicool.Conformance.Tests`
- `OpenLogicool.Domain.Tests`
- `OpenLogicool.Playbooks.Tests`
- `OpenLogicool.GameLab.Tests`
- `OpenLogicool.Host.Tests`
- `OpenLogicool.Architecture.Tests`

追加focused testは、Available＋Novel、frame-bound affordance、task plannerから独立したexploration proposal、delta proposalとtransition evidenceの分離、ObservedScene→Policy／Context→GameStructureRevision／GameStateFactのcontract graphを固定する。

`git diff --check`は改行変換警告以外の違反0。`ObservationStatus`、旧fixtureの`status: Known/Ambiguous/Unknown/Unavailable`、ObservationResult fixtureの旧`unavailableReason`の残存0を確認した。recognizer内部の失敗理由fieldはcapture境界の入力として維持し、ObservationResultでは`CaptureFailureReason`へ正規化する。

## 非対象

- append-only Structure Event Storeとprojectionは`t07`。
- coordinatorの実行順とcontroller validationは`t08`。
- t05で採用したlocal vision方式からObservedSceneを生成する製品providerは`t09`。
- このtaskは入力dispatchを行わないため、実game操作は不要。
