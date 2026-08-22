# t04 Resident output session evidence

- Date: 2026-08-23
- Task: `t04-resident-output-session`
- Evidence level: lifecycle実装＋fake exchangeで確認済み。実serial／実抜線／hard crash releaseは未確認。

## 成立した契約

- SendInputとSerial HIDのsession分離
- Serial HID routeでWindows watchdogを生成しない構造
- 50ms heartbeatとbackground faultのresident伝播
- handled stopのpump release→ALL_UP ACK→transport close
- heartbeat fault後のno retry、ALL_UP追加送信なし、暗黙resumeなし
- CLI runと`ui --resident`のfailure監視
- 起動時とUI書込み時のG600 onboard排他

## Focused test

```powershell
dotnet test tests/OpenLogicool.Host.Tests/OpenLogicool.Host.Tests.csproj --no-restore
dotnet test tests/OpenLogicool.Input.Tests/OpenLogicool.Input.Tests.csproj --no-restore
dotnet test tests/OpenLogicool.Architecture.Tests/OpenLogicool.Architecture.Tests.csproj --no-restore
```

- Host: 90件成功、失敗0
- Input: 141件成功、失敗0
- Architecture: 5件成功、失敗0
- `git diff --check`: 問題なし

## 未実測境界

実COM抜線、USB HID report、hard crashからfirmware lease all-upまでの時間は未確認。t06のfake clockとt07〜t09の実機gateで別々に受入する。
