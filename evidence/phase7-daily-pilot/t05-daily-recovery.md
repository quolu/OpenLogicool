# t05 Daily recovery

## 実施

- 二日 cycle の day2 session と既知 action path を既存 resume/fault 境界へ渡す `DailyRecoveryPlan` を追加した。
- Interrupted、ManualIntervention、ForegroundLost、CaptureLost、OutcomeUnknown の五原因を同じ再開候補へ写す。
- fault、foreground/capture 監視、daily reset、dispatch/input は再実装していない。

## 検証

```text
dotnet test tests/OpenLogicool.Playbooks.Tests/OpenLogicool.Playbooks.Tests.csproj --nologo --filter FullyQualifiedName~DailyRecoveryTests --logger console;verbosity=minimal

成功: 合格 5、失敗 0、スキップ 0
```

focused test で、五つの recovery trigger が day2 の既知 path を選び、day1 を Verified にしないことを確認した。
