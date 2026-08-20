# t04 LGS restore / rollback 証跡

## 実施

- `CancelDryRun` は LGS XML dry-run の convertible／unsupported 数を返し、apply を開始しない。
- cancel 結果は元 LGS profile が保持されたことを明示する。
- `RestoreG600Baseline` は既存 `G600LeftoverSession.Restore()` にのみ委譲し、G600 write/readback/retry を再実装しない。
- restore の device 不在・共存ソフト・baseline 不在・hard failure を `G600BaselineRestored` と表示しない。

## focused verification

| command | result |
| --- | --- |
| `dotnet test tests/OpenLogicool.Host.Tests/OpenLogicool.Host.Tests.csproj --nologo --filter 'FullyQualifiedName~MigrationRollbackTests' --logger 'console;verbosity=minimal'` | 3/3 green |
