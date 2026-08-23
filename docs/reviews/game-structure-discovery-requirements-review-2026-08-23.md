# AI Game Structure Discovery 要件レビュー

- 日付: 2026-08-23
- 対象正本: [development-plan.md](../development-plan.md) v0.4
- 位置付け: 調査・反証・裁定の記録。要件、architecture、Phase、release gateの正本はdevelopment-plan.mdだけであり、本書は複製正本ではない。
- 実装: 未着手

## 結論

game固有state、target、recognizer、transition、正解手順を開発者seedとして与えず、AIがframe-bound probeと再観測証拠からGame Structureを構築する要件を確定した。

現行Game OperatorのDurable Attempt、commit-before-dispatch、Run Journal、Run Controls、capture transform／freshness、AI proposal-only境界は再利用できる。一方、現行実装だけではzero-seed探索は成立しないため、Phase 9でExploration専用contract、Structure Event Store、projection、coordinator、vision／actuation admissionを追加する。

## 現行実装の確認結果

維持する成立済み基盤:

- Captureは画素付きFrame、transform revision、freshness、continuity faultを表現できる。
- Durable Attemptは外部入力前のDispatchArmed commit、OutcomeUnknown、自動再送禁止を持つ。
- Run Controlsはpause、一手実行、manual intervention、version pinを持つ。
- AIはproposal境界の外からInput／DB／device APIへ到達しない設計になっている。
- Screen GraphとPlaybookを別成果物にする思想は既に正しい。

zero-seedを阻む現行制約:

- `PlannerContext`は事前定義済みAllowedActionを要求し、`NextActionProposal`は既知stateを前提・期待結果に要求する。
- `ObservationResult.Unknown`はNovel sceneとaffordance evidenceを保持できない。
- `TeachAction.VisualTargetRef`はFrame／transformへ束縛されない文字列である。
- `FixtureFrameRecognizer`は開発者登録済みpixel hashからstateを返すfixture recognizerである。
- Knowledge Pack validatorは完成済みstate／graphのimportを前提とし、空graphを育てるportではない。
- Screen Graphはcontractだけで、builder、merge／split、replayed昇格、永続store、Host compositionがない。
- GameLabの遷移表、AllowedAction、ExpectedEventSequenceは手書きoracleであり、探索runtimeへ渡すと成立証拠を汚染する。
- current Input pathにgame画面上のpointer移動／target clickが成立した証拠はなく、live探索前の実測gateが必要である。

## Fable反証と反映

Fable 5 highへ二回、read-onlyで依頼した。

初回反証で採用した指摘:

- zero-seedの成立にはprovider／recognizer、graph蓄積・永続化、visual target actuationの3点が不足する。
- seed banをmachine testにし、GameLab oracleを最終assertionへ隔離する。
- AI proposal、controller commit、verification promotionを分離する。
- 未知targetの初回は一手承認とし、riskと復帰経路の証拠後にだけ自律度を上げる。

初回反証から変更して採用した裁定:

- 「key-only Teachまでに絞る」は最終目標を縮めるため採用しない。
- pointer／clickを後続へ先送りせず、Phase 9 G0のactual input gateに置く。
- 最初の一手承認は恒久的な人手前提ではなく、low-risk、side-effect-free、可逆、既知復帰を実証してbounded autonomous explorationへ進むための段階とする。

変更後の最終反証はCritical 0だった。指摘されたMajor 3件は正本へ反映した。

1. goal自由textからstate名／target名／正解routeを注入できる抜け道を閉じ、goal／prompt／conversationもseed inventoryへ含めた。
2. 構造commitの唯一の主体を`OpenLogicool.Exploration`内、Lane L所有のStructure Knowledge Controllerと定義した。
3. `ObservationKind`の2軸化をsemantic breaking migrationと明記し、Phase 5〜7のconsumer、fixture、conformance／frozen test再受入をPhase 9Aへ追加した。

Minor指摘も反映し、Personal Knowledge Storeを用語定義し、AI-005の検証主体、Game State Factの有効条件、generic keyへのgame固有語義seed禁止を明示した。

## 次の技術判定

実装開始点はPhase 9 G0だけである。

1. zero-seed visual discoveryが可能なprovider／recognizerを凍結metricで選定する。
2. frame／crop／OCR／embeddingのData Flowと同意・削除を確定する。
3. GameLabとNIKKEでpointer、frame-bound click、back／Escape、scroll、generic keyのactual acceptanceをroute別に実測する。
4. NIKKE lobby safe sliceのGame Policyとnon-impact boundaryを固定する。

このadmissionが成立するまで、Structure builderやlive自動探索を先に実装しない。
