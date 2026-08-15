# KnowledgePackManifest contract draft（Phase 0 / 2026-08-15）

Knowledge Pack（data のみ・実行 plugin なし、§6.11）の manifest。意味 owner は Perception/Knowledge（Lane G）。本書は Phase 0 の draft。

fixture: [fixtures/contracts/knowledge-pack-manifest.sample.json](../../fixtures/contracts/knowledge-pack-manifest.sample.json)

## フィールド

| field | 型 | 意味 |
|---|---|---|
| schemaVersion | string (semver) | manifest schema の版 |
| packId | string (stable ID) | pack の恒久 ID |
| packVersion | string (semver) | pack 内容の版。immutable、更新は新 version |
| game | object | game 名・build 識別・locale |
| supportedEnvironments | array | 環境 scope tuple（§6.8）: build, locale, UI scale, resolution, display mode, DPI, HDR, capture backend, input route, Screen Graph version, recognizer version |
| sections | object | 同梱節の相対 path と content hash: application-identities, semantic-actions, states, recognizers, visual-targets, screen-graph, playbooks, fixtures, policy-record, migrations |
| provenance | object | 作成者、作成日時 UTC、出典、license 表記 |
| trust | enum | import 直後は常に `Untrusted`。昇格は利用側の検証で行い、pack 自身は宣言できない |
| migrations | array | 旧 packVersion からの data migration 記述（data のみ。script 不可） |

## 意味規則（計画からの継承）

- **単一 state ID 原則**: `states` は Screen Graph の node 台帳であり、Playbook 前提・Observation の Known 候補・visual target 帰属・Screen Graph node はすべて同じ stable state ID を参照する。同じ画面状態を別 ID で二重定義しない（§6.11）。
- **data のみ**: code・script・prompt override・provider 設定・secret を含む pack は受理しない。`trust=Untrusted` からの昇格条件に「禁止内容の不在検査」を含める。
- candidate/verified 状態は pack 内では candidate として扱い、verified への昇格証拠は import 先の live session だけが与える（§6.8、KP-005）。pack が verified を主張しても消費側は candidate へ降格して読む。
- sections の hash 不一致は import 失敗として明示し、部分 import しない。

## 未決定

- sections 各節の内部 schema（本 manifest draft の範囲外。states / screen-graph は Phase 1 GameLab で fixture 先行）
- pack の配布形態（単一 zip か directory か）と署名の要否
- migrations の表現力（field rename までか、値変換を許すか）
