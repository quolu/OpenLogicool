# NextActionProposal contract draft（Phase 0 / 2026-08-15）

AI が Playbook 実行系へ返す唯一の出力。意味 owner は AI（Lane H）だが、検証と dispatch は Runtime が所有し、AI は入力・DB・device API へ直接到達しない（fast path 純潔）。本書は Phase 0 の draft。

fixture: [fixtures/contracts/next-action-proposal.sample.json](../../fixtures/contracts/next-action-proposal.sample.json)

## フィールド

| field | 型 | 意味 |
|---|---|---|
| schemaVersion | string (semver) | 本 contract の版 |
| proposalId | string (stable ID) | 一意。Attempt（Proposed 状態）への相関キー |
| plannerContext | object | 参照した PlannerContext の要約: goal, 現在 observationId, budget 残 |
| mode | enum | `VerifiedRun` / `Teach` |
| action | object | mode 別 payload（下記） |
| precondition | object | state predicate（stateId＋観測鮮度条件）。不成立なら dispatch しない |
| expectedOutcome | object | 成功と判定する状態（stateId または predicate）と安定窓条件 |
| stopCondition | object | 打切り条件（timeout ms・観測不能・予算到達） |
| validity | object | pin: 参照 Frame sequence と transformRevision。revision が変わった proposal は失効 |

### action payload（mode 別）

- `VerifiedRun`: `semanticActionId` のみ。**既存 action catalog の ID だけ**を許可し、座標・key code・新規 action を含められない（§6.10）。
- `Teach`: `visualTargetRef`（Perception が現在 frame から列挙した visual target の ID）＋ `primitive`（事前許可 primitive の識別子）。AI が任意座標を生成することはない。Runtime は target が同じ Frame／transform revision へ属し、対象 window 内で、許可 primitive であることを検証する。

## 意味規則（計画からの継承）

- AI は action catalog・risk class・execution mode・provider/model・cloud 送信範囲・cost cap・game policy・verified status を変更できない（§6.10）。これらのフィールドは本 contract に存在しないことで保証する（値として運べない）。
- provider error / timeout / rate limit / schema error / budget 到達は明示停止し、別 provider へ fallback しない。この場合 proposal は生成されず、失敗理由が Attempt へ記録される。
- schema 不一致の proposal は Runtime が Rejected として記録する。丸め・補完して受理しない。

## 未決定

- precondition / expectedOutcome の predicate 表現（stateId 単独か、述語言語を持つか）
- Teach mode の許可 primitive 一覧（click / drag / key の粒度）。primitive は現時点で string 識別子であり、enum 化は未決定
- proposalId と Attempt ID の対応多重度（1:1 固定か、再試行で 1:N か）
