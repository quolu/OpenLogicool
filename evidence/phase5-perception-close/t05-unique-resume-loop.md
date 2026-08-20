# t05-unique-resume-loop 証跡

`CaptureContinuityDispatch` に `TryResumeStepOnce` を追加し、LiveResumeGate が拒否した時は既存 dispatch／外部入力へ到達しないようにした。

最終試験:

`dotnet test tests/OpenLogicool.Host.Tests/OpenLogicool.Host.Tests.csproj --nologo --filter "FullyQualifiedName~LiveResumeDispatchTests" --logger "console;verbosity=normal"`

- 結果: 4/4 passed。
- Ambiguous／Unknown／Unavailable は外部入力 0 回。自前 WinForms window の WGC frame では UniqueMatch と3対象一致で1回だけ dispatch、input target 不一致は dispatch しない。
- `git diff --check` は出力なし。
