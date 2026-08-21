# Eval threshold record 契約

`EvalThresholdRecord` は既存 `EvalHarness` の `FrozenEvaluationReport` を実行せず、AI eval の入力来歴と事前固定した受入閾値へ結び付ける。

- 実行前に dataset ID／version／corpus digest、model ID、prompt ID／digest、parameter を `EvalInputRecord` として固定する。parameter は記録時にコピーするため、呼出元の後続変更は記録へ反映されない。
- provider を表す field・選定口は持たない。model ID は候補を識別する記録であり、provider 採択ではない。
- `EvalThreshold` は既知 action 正答率・未知 action 拒否率・合計 latency・合計 cost の閾値を固定する。評価後に corpus や prompt を調整する口は持たない。
- `Assess` は中断、対象 case 欠落、各閾値未達を個別の failure として残す。全 failure が無い時だけ `IsAccepted` である。
- corpus 評価そのものは既存 `EvalHarness` の責務であり、本契約は再実装しない。
