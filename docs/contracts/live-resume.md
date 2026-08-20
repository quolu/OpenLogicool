# Live resume contract（Phase 5 t09）

`LiveResumeGate` は t06 の `ObservationResult` を Phase 4 の `StateMatcher` と `ResumeGate` に渡す、dispatch 前の pure gate である。InputEmitter を参照せず、許可結果だけを返す。

## 許可条件

- Observation は `StateMatcher` で `UniqueMatch` になる。Known 以外、鮮度超過、安定継続時間不足、期待 state 不一致は許可しない。
- 記録済み app identity と観測 app identity、記録済み capture source と観測 capture source が一致する。
- 記録済み target window と観測 target window が一致する。
- 観測 capture source は `ObservationResult.Frame.SourceId` と一致する。
- input target window は観測 target window と一致する。
- Phase 4 の run closed、version drift、manual intervention 後の未記録 observation をそのまま拒否する。

不一致は `LiveResumeDecision` の理由として明示し、false の結果を受けた呼び出し側は dispatch してはならない。静かな別 source／window への fallback はしない。
