# t10 Hidden-oracle GameLab 実装・検証記録

取得日時: 2026-08-24
対象: Phase 9B / `t10-hidden-oracle-gamelab`
状態: **成立**

## 結論

Web Reference 0件、game固有seed 0件のpixel-only hidden-oracle GameLabを実装し、空SQLite DBから3 node・4 edgeを発見した。no-change、loop、crash、OutcomeUnknown、capture loss、stale、budget、recovery loss、restart、別session再同定とverification昇格をfocused scenarioで成立させた。

## hidden oracle境界

- GameLab内部だけが3状態のoracle graph、正解click領域、no-change、loopを所有する。
- 探索runtimeへ渡す`IGameLabVisualSurface`はgray pixel frame、source／frame／transform／freshness／capture availabilityとgeneric normalized clickだけを公開する。
- runtime側`ZeroSeedFrameRecognizer`はpixel SHA-256をstate hypothesisとし、明るい連結領域だけをframe-bound `AffordanceCandidate`へ変換する。oracle state ID、action名、正解target、expected sequenceを入力しない。
- 専用test projectは別testhost processで実行され、探索sessionはinterface capabilityだけを受け取る。oracle auditはtest側の受入判定にだけ使う。
- machine testでruntimeへ渡した`ObservedScene` JSONに`oracle-alpha`／`oracle-beta`／`oracle-gamma`が含まれないことを確認した。

## zero-seed発見結果

実SQLite fileをmigration済み空DBから開始し、次の順で探索した。

1. 初期pixel signatureからCandidate nodeを作成。
2. 同一画面の別affordanceをprobeし、`NoChange` evidenceとself-edgeを保存。
3. generic clickで第2画面を発見し、新nodeとedgeを保存。
4. 第3画面を発見し、新nodeとedgeを保存。
5. 第3画面から初期pixel signatureへ戻るloopを再同定し、loop edgeを保存。

最終projectionはnode 3件、edge 4件、no-change 1件、異なるoracle state 3件、初期画面へのloop成立である。node／edgeはすべてCandidateから開始し、oracleの名前をstable IDに使用しない。

## fault／停止scenario

- **crash**: `DispatchArmed` commit後、GameLabがclickを1回受理した直後に例外。自動再送0、再open後のRun AttemptとStructure dispatchはいずれも`OutcomeUnknown`。
- **capture loss**: dispatch後のUnavailable sceneを安定成功へ丸めず、Run Attemptはrestart分類で`OutcomeUnknown`。Confirmation 0、click 1回。
- **stale**: freshness上限超過をproposal時に停止。dispatch 0。
- **budget**: elapsed budget超過をproposal時に停止。dispatch 0。
- **recovery loss**: deterministic riskが示す復帰edgeを現在Contextで確認できないため停止。dispatch 0。
- **restart**:同じSQLite fileを別connectionで再openし、append-only Structure revisionとRun Journalだけから未解決Attemptを復元。

blind retryは全scenarioで0、scope外dispatchは0である。

## 別session再同定と昇格

初回sessionのpixel signatureを保存済みnodeのscene signature集合へ照合し、2回目sessionで同じstable node IDを再同定した。2回目sessionのTransition EvidenceでCandidate→Replayed、さらに異なる3回目sessionのTransition EvidenceでReplayed→Verifiedへ一段ずつ昇格した。

`StructureVerificationController`はAI delta authorityと分離し、次を要求する。

- discovery sessionとreplay sessionが異なること
- 指定session IDを持つ実在Transition Evidence
- Candidate→Replayed→Verifiedの一段遷移
- Verified昇格時はReplayed昇格と異なる独立session

同一session、段飛ばし、存在しないevidenceは昇格せずCandidateを保持する。AIの`StructureDeltaProposal`からReplayed／Verifiedへ昇格する経路は引き続き存在しない。

## focused test

- `OpenLogicool.GameLab.Discovery.Tests`: 9件 green
- `OpenLogicool.Exploration.Tests`: 16件 green
- `OpenLogicool.GameLab.Tests`: 24件 green
- `OpenLogicool.Persistence.Tests`: 47件 green
- `OpenLogicool.Conformance.Tests`: 57件 green
- `OpenLogicool.Architecture.Tests`: 8件 green
- `dotnet build OpenLogicool.sln --no-restore --nologo`: 成功

## 次工程との境界

t10はhidden oracle上のzero-seed、fault、restart、別session verificationを受入済みにした。t11はこのprojectionとCoordinatorをHost／Desktopへ配線し、structure revision、Known／Novel、frontier、probe、risk／承認理由、budget、復帰経路、停止理由、verification状態とpause／step／abandon／訂正を利用者面へ出す。
