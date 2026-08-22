# t06 Focused / fake gates evidence

- Date: 2026-08-23
- Task: `t06-focused-and-fake-gates`
- Evidence level: pure/fake/native-host確認済み。実boardは未flash。

## Acceptance matrix

| 項目 | 結果 | 証拠 |
|---|---|---|
| C#／firmware共通golden vector | 確認済み | JSON 7 vectorをC# testと実`ProtocolV1.cpp`へ入力 |
| partial read／magic再同期 | 確認済み | `SerialHidResponseFrameAssembler` chunk test |
| checksum／unknown version | 確認済み | C# codec＋firmware native decoder |
| ACK timeout／sequence mismatch／FAULT | 確認済み | `SerialHidEmitterTests`、ACK前commitなし・no retry |
| chord／finite sequence | 確認済み | checkpointごとのsnapshot／ACK順 |
| duplicate ownership／wrong up | 確認済み | reference count、wrong up明示fault |
| 6KRO／unsupported usage | 確認済み | 7個目wire送出なし、未知usage commitなし |
| handled stop | 確認済み | release→ALL_UP ACK→close |
| hard-crash lease fake clock | 確認済み | firmware共有`FirmwareLease`の149/150ms＋wraparound |
| no fallback | 確認済み | discovery failureがterminal、SendInput sessionを生成しない |
| SendInput characterization | 確認済み | 既存`SendInputKeyboardPlanTests`を含むInput focused green |
| Host／Desktop設定scenario | 確認済み | 接続成功時だけ保存、失敗時保存維持、requested/active、次回factory適用 |

## Commands

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test-serial-hid-firmware.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/build-serial-hid.ps1
dotnet test tests/OpenLogicool.Input.Tests/OpenLogicool.Input.Tests.csproj --no-restore
dotnet test tests/OpenLogicool.Host.Tests/OpenLogicool.Host.Tests.csproj --no-restore
dotnet test tests/OpenLogicool.Desktop.Tests/OpenLogicool.Desktop.Tests.csproj --no-restore
dotnet test tests/OpenLogicool.Architecture.Tests/OpenLogicool.Architecture.Tests.csproj --no-restore
```

- firmware native: 7 golden vector、checksum/version fault、lease 150ms成功
- firmware compile: 成功（program 6080 byte / global 256 byte）
- Input 141、Host 105、Desktop 77、Architecture 6、失敗0
- `git diff --check`: 問題なし

## 未実測境界

CDC serial write、物理ACK、USB HID report、foreground受理、game内成功、hard kill後の実release時間は未確認。t07〜t09で各層を分けて判定する。

