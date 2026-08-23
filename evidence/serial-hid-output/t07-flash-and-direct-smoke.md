# t07 Flash and direct Serial HID smoke evidence

- Date: 2026-08-23
- Task: `t07-flash-and-direct-smoke`
- Machine: `FOX`
- Board: SparkFun Pro Micro ATmega32U4 5V / 16MHz
- Composite identity: `USB\VID_1B4F&PID_9206\HIDFG`

## Acceptance matrix

| 項目 | 判定 | 証拠 |
|---|---|---|
| firmware compile | 確認済み | 6080 byte flash、256 byte RAM |
| target identity | 確認済み | exact present composite instanceを一意に照合 |
| flash／verify | 確認済み | `serial-hid-flash-20260823-041851-365.json`、uploadVerified=true |
| CDC／keyboard／mouse列挙 | 確認済み | 同じContainerId配下の3 interfaceがbefore／afterともpresent |
| HELLO／READY | 確認済み | firmware 1.0.0、capabilities 0x0007、lease 150ms |
| key | 確認済み | F13 down/upを非injected keyboard hookで観測 |
| chord | 確認済み | Left Ctrl + F14の完全down/upを観測 |
| mouse button | 確認済み | middle down/upを非injected mouse hookで観測 |
| finite sequence | 確認済み | F15 tap → Left Ctrl + F16の順序を観測 |
| ALL_UP | 確認済み | F17保持後の明示ALL_UPでrelease |
| timeout release | 確認済み | F18保持後、CDC closeから149.465msでrelease |
| power cycle all-up | 確認済み | disconnect／reconnectをPnP identityで観測、unexpected down 0 |
| post-cycle direct smoke | 確認済み | 再列挙後にPnPから一時portを再解決し全direct check再PASS |
| SendInput fallback | 非対応 | direct smoke／flash経路にfallbackなし |
| foreground app受理 | 未確認 | t08で別判定 |
| 対象game内成功 | 未確認 | t09で別判定 |

## Structured evidence

- Flash／列挙: `probe-output/serial-hid-flash-20260823-041851-365.json`
- Power cycleを含むdirect smoke: `probe-output/serial-hid-direct-smoke-20260823-104759-557.json`
- 再列挙後direct smoke: `probe-output/serial-hid-direct-smoke-20260823-104818-711.json`

`transientPort`は実行時transportの記録であり、製品identityではない。power-cycle判定はcomposite device instance IDを使った。

## Commands

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/build-serial-hid.ps1
pwsh -NoProfile -File scripts/flash-serial-hid.ps1 -DeviceInstanceId 'USB\VID_1B4F&PID_9206\HIDFG'
dotnet run --project src/OpenLogicool.Probe/OpenLogicool.Probe.csproj --no-build -- serial-hid-direct-smoke --port COM8 --device-instance-id 'USB\VID_1B4F&PID_9206\HIDFG' --power-cycle
```

Flash scriptはexact target、同一ContainerId、unique transient CDC、固定FQBN、upload verify、再列挙を一つの操作で検証する。自動bootloader捕捉が成立したためdouble-resetは不要だった。

## 復旧

現在のfirmwareを戻す場合は、同じexact board identityを確認し、Caterina bootloaderをdouble-resetで開いて既知green sketch／hexを再flashする。target identityが曖昧な場合は書き込まない。

## 証拠境界

- serial writeとmatching ACK: 確認済み
- Windowsが非injected keyboard／mouse eventとして受理: 確認済み
- raw USB report byteの独立capture: 未確認
- 特定foreground app受理: 未確認
- 対象game内成功: 未確認

これらを相互の代用にせず、後続Taskで別々に判定する。
