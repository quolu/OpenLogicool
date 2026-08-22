# t05 Discovery / settings / UI evidence

- Date: 2026-08-23
- Task: `t05-discovery-settings-ui`
- Evidence level: Windows API配線＋fake transportで確認済み。実boardは未接続・未flash。

## 成立した契約

- `GUID_DEVINTERFACE_COMPORT`のSetupAPI列挙とSparkFun Pro Micro VID/PID候補限定
- PnP device instance IDの永続化、COM番号identityの拒否
- HELLO／READY成功がちょうど1台の時だけsession確定
- schema `1.0`のmachine-local route設定
- requested routeとactive routeの分離表示
- Serial HID保存前の接続test、日本語status、次回resident起動反映
- CLI `run`／`ui --resident`のoutput session factory配線
- Serial HID設定面からraw input sourceを増やさない構造
- G600 onboard modeとの既存排他、SendInputへのfallbackなし

## Focused test

```powershell
dotnet test tests/OpenLogicool.Host.Tests/OpenLogicool.Host.Tests.csproj --no-restore
dotnet test tests/OpenLogicool.Input.Tests/OpenLogicool.Input.Tests.csproj --no-restore
dotnet test tests/OpenLogicool.Desktop.Tests/OpenLogicool.Desktop.Tests.csproj --no-restore
dotnet test tests/OpenLogicool.Architecture.Tests/OpenLogicool.Architecture.Tests.csproj --no-restore
```

- Host: 100件成功、失敗0
- Input: 141件成功、失敗0
- Desktop: 77件成功、失敗0
- Architecture: 6件成功、失敗0

## 判定を分離した未実測境界

- SetupAPI candidate列挙: 製品コードとidentity parserは確認済み、実board列挙は未確認
- serial write／READY ACK: fake exchangeで確認済み、物理CDCは未確認
- USB HID report: 未確認
- foreground受理: 未確認
- game内成功: 未確認

実firmware compile／flash／列挙／direct smokeはt07、G13／G600 end-to-endはt08、hard killとgame観測はt09で別々に判定する。
