# t03 Serial HID host core evidence

- Date: 2026-08-23
- Task: `t03-serial-hid-core`
- Evidence level: pure core＋fake frame exchangeで確認済み。実serial／USB HIDは未確認。

## 実装

- 一往復だけを表す`ISerialHidFrameExchange`
- HELLO／READY handshakeとrequest sequenceを所有する`SerialHidProtocolSession`
- timeout／transport／protocol破損／FAULT／unexpected responseのterminal fault化
- SET_STATE sequenceを受けてtentative snapshotを作る`SerialHidEmitter`
- matching ACK後だけのownership commit
- chord／finite sequenceのcheckpoint順序維持
- duplicate ownership参照数、modifier／normal key／mouse buttonの完全snapshot
- 6KRO超過、wrong up、unsupported usageのwire前拒否
- 自動再送なし、SendInput fallbackなし

## Focused test

```powershell
dotnet test tests/OpenLogicool.Input.Tests/OpenLogicool.Input.Tests.csproj --no-restore
dotnet test tests/OpenLogicool.Architecture.Tests/OpenLogicool.Architecture.Tests.csproj --no-restore
```

- Input: 141件成功、失敗0
- Architecture: 5件成功、失敗0
- `git diff --check`: 問題なし

## 未実測境界

実serial portのpartial read、物理ACK、USB HID report、foreground受理、game内成功は本taskの成立証拠に含めない。port discovery／transportはt05、fake gateの拡充はt06、実機flash以降はt07〜t09で受入する。
