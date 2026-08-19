# RunEvent contract — Phase 4 / revision 0.1（Phase 1 draft から昇格）

Semantic owner は Playbooks。RunEvent は durable journal の event を表し、ObservationResult 自体へ Attempt を持たせず、Attempt と Observation の束縛はこの event に記録する。

## field

| field | type | meaning |
|---|---|---|
| schemaVersion | string | contract schema version |
| eventId | string | stable event ID |
| runId / runSequence | string / long | run とその順序 |
| playbookId / playbookVersionId | string / string | immutable Playbook と採用 version |
| nodeOrTransitionId | string? | node または transition の stable ID |
| commandId | string? | dispatch command ID |
| attemptId | string? | dispatch attempt ID |
| causationId / correlationId | string / string | causation と correlation の追跡 ID |
| executorEpoch | long | active executor の epoch |
| actorType | enum | User / Automation / System |
| occurredUtc / persistedUtc | DateTimeOffset / DateTimeOffset | 発生時刻と永続化時刻 |
| observationId | string? | Observing 以降に束縛する Observation ID |
| payloadType / payloadJson | string / string | typed payload の種別と内容 |

## nullability の決定（Phase 4 t03 で最終決定）

`attemptId`、`commandId`、`nodeOrTransitionId` は schema 上 null 許容を維持する（run 開始等の Attempt 以前の event で存在しないため）。計画 §6.7 の必須 field 表記との整合は、schema でなく journal の append 検証（`RunJournal`）が payload type ごとに持つ:

| payload type | 必須 ID |
|---|---|
| observation | observationId |
| confirmation | attemptId ＋ observationId（§6.7 契約4の併記） |
| dispatch | attemptId ＋ commandId |
| dispatch-result | attemptId |

`observationId` は Observing より前の event で存在しない。これは schema 初版からの null 許容であり、計画 §6.7 の明示仕様である。

## journal payload type（PB-006・t03 確定）

journal（`IRunJournalStore`）に保存できる payload type は次の閉集合だけであり、未知の種別は append 時に拒否される: `observation`、`proposal`、`approval`、`dispatch`、`dispatch-result`、`confirmation`、`correction`、`manual-intervention`。訂正（correction）は新しい event の追記であり、確定済み event は変更されない。
