# RunEvent contract draft — Phase 1 / revision 0.1

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

## nullability の未決定事項

run 開始等の Attempt 以前の event では `attemptId`、`commandId`、`nodeOrTransitionId` が存在しないため、これらは null 許容とする。計画 §6.7 の必須 field 表記との整合は、Phase 4（Playbook 実装）で最終決定する。

`observationId` は Observing より前の event で存在しない。これは schema 初版からの null 許容であり、計画 §6.7 の明示仕様である。
