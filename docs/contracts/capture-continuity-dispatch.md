# Capture continuity dispatch

Host の `CaptureContinuityDispatch` は `CaptureContinuityGate` の許可を、Playbooks の `RunControls.StepOnce` より前に読む。

- stale、backend change、resize の後は `false` を返し、Attempt を arm せず外部入力を呼ばない。
- WGC の変化待ちによる fault なしの無 frame は連続性を切らず、既に校正済みなら dispatch を止めない。
- FastPathPump は参照しない。capture の不連続を別 backend へ切り替えて回避しない。
# Capture continuity dispatch contract

`CaptureContinuityDispatchLoop` は Host の非 fast-path 境界で、`CaptureRead` を `CaptureContinuityGate` へ反映してから `CaptureContinuityDispatch` を呼ぶ。再校正は caller が同じ最新 frame を明示した時だけ行い、fault・stale・backend／transform 変更後に自動で解除しない。

`OpenLogicool.Host capture-dispatch <continuity|resume>` は、この製品経路を一回駆動する CLI 入口である。`resume` は同じ loop 内で `LiveResumeGate` と continuity gate の両方を通る。`FastPathPump` は参照しない。

CLI の外部 input は現段階ではコンソールへの handoff 記録であり、OS input を合成しない。これは input executor の代替を偽装しないためで、許可・停止の境界を Host から観測する入口だけを提供する。
