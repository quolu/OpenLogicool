# t08 — Eval threshold record

## 実装

- `EvalThresholdRecord` を既存 `EvalHarness` の report を評価後に記録・判定する pure contract として追加した。
- evaluation 前に `EvalInputRecord` で frame dataset の ID／version／digest、model ID、prompt ID／digest、parameter のコピーを固定する。
- known action accuracy、unknown rejection rate、total latency、total cost の threshold を固定し、中断・case 欠落・各未達を個別 failure として残す。
- provider の field や選定口、corpus／prompt を評価後に調整する口、EvalHarness の実装は追加しない。

## Focused verification

Windows native の worktree 内で実行:

```powershell
dotnet test tests/OpenLogicool.AI.Tests/OpenLogicool.AI.Tests.csproj --no-restore --filter FullyQualifiedName~EvalThresholdRecordTests --logger console;verbosity=normal
```

結果: exit 0（focused green）。

- 事前固定した dataset／model／prompt／parameter と threshold が既存 eval report の acceptance を決める。
- 中断、正答率・拒否率・latency・cost の未達を individual failure として記録する。
- threshold 対象 case の欠落と不正な parameter／threshold を拒否する。
