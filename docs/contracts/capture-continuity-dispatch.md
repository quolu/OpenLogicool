# Capture continuity dispatch

Host の `CaptureContinuityDispatch` は `CaptureContinuityGate` の許可を、Playbooks の `RunControls.StepOnce` より前に読む。

- stale、backend change、resize の後は `false` を返し、Attempt を arm せず外部入力を呼ばない。
- WGC の変化待ちによる fault なしの無 frame は連続性を切らず、既に校正済みなら dispatch を止めない。
- FastPathPump は参照しない。capture の不連続を別 backend へ切り替えて回避しない。
