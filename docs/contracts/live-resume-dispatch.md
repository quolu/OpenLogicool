# Live resume dispatch contract（Phase 5 close / t05）

`CaptureContinuityDispatch.TryResumeStepOnce` は、既存の Capture 連続性確認と同じ外部入力直前で `LiveResumeGate` を読む。

- `UniqueMatch`、recorded／observed target window、capture source、input target の全一致、version・再観察・run 状態の既存条件が揃う時だけ dispatch する。
- Ambiguous、Unknown、Unavailable、stale、target/capture/input の不一致では外部入力 delegate を呼ばない。
- NIKKE 実画面は対象外。自前 window の WGC frame で native conformance を行う。
