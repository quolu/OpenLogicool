# t01-resident-dispatch-loop

## 実装

Host に `CaptureContinuityDispatchLoop` を追加した。各一手は `CaptureRead` を
`CaptureContinuityGate` へ反映し、caller が明示した最新 frame だけで再校正してから、既存の
`CaptureContinuityDispatch` を通す。resume は `LiveResumeGate` と continuity gate の両方を
通る。`FastPathPump` は参照しない。

`OpenLogicool.Host capture-dispatch <continuity|resume>` を製品 CLI 入口として追加した。
これは Host の dispatch 境界を一回駆動して許可／停止と handoff を表示する。現時点の CLI は
OS input を合成せず、承認済み handoff を明示記録するだけである。

## 最終確認

- `dotnet test tests/OpenLogicool.Host.Tests/OpenLogicool.Host.Tests.csproj --nologo --filter "FullyQualifiedName~CaptureContinuityDispatchTests|FullyQualifiedName~LiveResumeDispatchTests" --logger "console;verbosity=minimal"`
  - 9/9 green。
  - loop が read→明示再校正→dispatch の順序を保ち、既存の continuity と live resume focused test も green。
- `dotnet run --project src/OpenLogicool.Host/OpenLogicool.Host.csproj --no-build -- capture-dispatch continuity --recalibrate`
  - 許可、handoff あり。
- `dotnet run --project src/OpenLogicool.Host/OpenLogicool.Host.csproj --no-build -- capture-dispatch resume --recalibrate`
  - 許可、handoff あり。resume と continuity の二重 gate を通過。
- `dotnet run --project src/OpenLogicool.Host/OpenLogicool.Host.csproj --no-build -- capture-dispatch continuity --capture stale`
  - exit 2、停止、handoff なし。
- `git diff --check`
  - 空白エラーなし（Windows の改行変換警告のみ）。
