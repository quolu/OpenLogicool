# t04-continuity-dispatch

## 実装

Host の製品統合境界に `CaptureContinuityDispatch` を追加した。`CaptureContinuityGate` が許可しない時は `RunControls.StepOnce` へ進まず、Attempt を arm せず外部入力 delegate を呼ばない。許可時は既存の Playbooks dispatch 経路だけを使う。

`Capture` は Windows 10.0.22621 を要求し、Playbooks が直接参照すると architecture の許可行列を破るため、両方を参照可能な Host に統合した。FastPathPump は参照していない。

## 最終確認

- `dotnet test tests/OpenLogicool.Host.Tests/OpenLogicool.Host.Tests.csproj --filter "FullyQualifiedName~CaptureContinuityDispatchTests"` — 4/4 green。stale、backend change、resize が dispatch 前で停止し、静止中の fault なし無 frame は校正済みの dispatch を止めない。
- `dotnet test X:\tests\OpenLogicool.Host.Tests\OpenLogicool.Host.Tests.csproj` — 53/53 green。worktree の絶対パスが長いと SQLite native DLL 読込が失敗する既知の Windows 制約があるため、一時 `subst X:` で同一 worktree を短縮して測定し、直後に解除した。
- `dotnet test tests/OpenLogicool.Capture.Tests/OpenLogicool.Capture.Tests.csproj` — 15/15 green。
- `dotnet test tests/OpenLogicool.Architecture.Tests/OpenLogicool.Architecture.Tests.csproj` — 4/4 green。Playbooks→Capture の禁止境界を保ち、Host の既存許可参照だけで統合している。
- `git diff --check` — 空白エラーなし。

実 game／NIKKE の観測は実施していない。この ToDo の対象は製品 dispatch の focused test までであり、Windows native の resume 実測は後続 t05 の範囲である。
