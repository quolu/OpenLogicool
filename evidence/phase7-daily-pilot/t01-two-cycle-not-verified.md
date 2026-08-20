# t01 Two-cycle, not Verified

## 実施

- 既存 GameLab daily reset には触れず、virtual day が連続する二つの session を記録する `DailyTwoCycle` を追加した。
- day2 は day1 と別 session かつ同一 environment で、day1 の成功 action path を replay しなければならない。
- 初日の成功を表す `DayOneVerified` は常に `false` とした。

## 検証

```text
dotnet test tests/OpenLogicool.Playbooks.Tests/OpenLogicool.Playbooks.Tests.csproj --nologo --filter FullyQualifiedName~DailyTwoCycleTests --logger console;verbosity=minimal

成功: 合格 2、失敗 0、スキップ 0
```

focused test で、別 session の翌日 replay と初日非 Verified、同一 session／異なる path の拒否を確認した。
