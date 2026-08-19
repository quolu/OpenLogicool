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
| skip | nodeOrTransitionId（§6.8: どの手順を飛ばしたかが本体） |
| disarm | attemptId（§6.7: どの Attempt を保証付きで止めたかが本体） |

`observationId` は Observing より前の event で存在しない。これは schema 初版からの null 許容であり、計画 §6.7 の明示仕様である。

## journal payload type（PB-006・t03 確定、run 制御3種は t05・disarm は t07 追加）

journal（`IRunJournalStore`）に保存できる payload type は次の閉集合だけであり、未知の種別は append 時に拒否される: `observation`、`proposal`、`approval`、`dispatch`、`dispatch-result`、`confirmation`、`correction`、`manual-intervention`、`skip`、`abandon`、`version-switch`、`disarm`。訂正（correction）は新しい event の追記であり、確定済み event は変更されない。

## run 制御 event（PB-007／PB-013・t05 確定）

- `skip`: 手順1個を実行せず飛ばした記録（§6.8「skipを別eventにする」）。dispatch も Attempt も作らない。
- `abandon`: Run 単位の中止。この event 以降、同じ Run へ event は積まれない。復元時、この Run の未確定 Attempt は dispatch 前なら Cancelled、dispatch し得た後なら Abandoned へ分類される（Attempt ごとの終端 event は持たない——run 単位の abandon event が正）。
- `version-switch`: 正規の version 切替（§6.8: Paused かつ現在 state 再照合後だけ）。event の `playbookVersionId` が切替後の新 version を運ぶ——pin と異なる version を運んでよい唯一の event であり、以後の event は新 version を運ぶ。切替前 version は payload に記録する。
- `manual-intervention` は開始・終了の2 event として現れる（区別は payload。ID field は同形）。開始と終了の間に `observation` event は現れない（介入開始で executor が止まり、run-level 観測の記録も拒否される）。終了 event の後、新しい `observation` event が記録されるまで Run は進行しない（§6.8）。再開照合（PB-009・t10）はこの並びを前提に「最後の manual-intervention event より後の observation」を新しい観測と読む。
- pause／resume は journal 対象外: durable な進行効果を持たず（再起動後に自動で走り出す経路が無い）、記録すべき「進行の変更」が無い。
- run 制御 event（skip・abandon・version-switch・manual-intervention）の `actorType` は `User` だけである（PB-013: 制御操作を自動化へ帰属させない）。skip・abandon・version-switch は journal の append 検証が拒否し、manual-intervention は制御経路（`RunControls`）が拒否する（t03 確定の journal 検証を遡って変えない）。

## fault 解決 event（§6.7・NFR-012・t07 確定）

- `disarm`: DispatchArmed 後、外部入力 API を一度も呼んでいないことを runtime 自身が保証できる場合だけの中止終端の記録。保証根拠（handled stop・対象 window 喪失等）は payload に記録する。`actorType` は `System` だけ（runtime の保証判定であり、利用者操作でも自動化の成功でもない——journal の append 検証が拒否する）。復元時、disarm event のある Attempt は Disarmed のまま（OutcomeUnknown へ劣化しない）。partial SendInput は外部入力 API が呼ばれた事実そのものであり、disarm では表現できない（分類は `AttemptFaultClassifier` が矛盾を例外にする）。
- OutcomeUnknown は journal event を持たない: 「dispatch 済みで解決の記録が無い」ことが OutcomeUnknown の定義そのもの（§6.7 契約2）であり、復元の既定分類と live の分類が同じ根拠を読む。
- 外部効果の回数は 1 と仮定しない（§10.2）: 0 回保証＝Disarmed（disarm event）、報告あり＝DispatchReported（dispatch-result event）、partial／unknown＝OutcomeUnknown（event なし）で表現する。
