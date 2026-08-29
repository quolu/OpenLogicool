# Serial HID Output 運用・復旧手順

- 対象: OpenLogicool Input Studio の USB出力（Serial HID v1）
- 確認済み環境: Windows 11 build 26200 / x64、SparkFun Pro Micro ATmega32U4 5V / 16MHz、firmware 1.1.3
- device identity: `USB\VID_1B4F&PID_9206\HIDFG`
- protocol: v1、keyboard 6KRO、mouse button 5個、relative pointer／wheel、firmware lease 150ms

## 通常運用

1. Pro Micro、G13、G600を接続する。
2. LGS、G HUB、Logi Options+を終了する。LGSは将来のLCD調査用にインストールしたままでよいが、自動起動を無効にし、Serial HID運用中は`LCore.exe`を起動しない。
3. Input Studioを`ui --resident`で起動する。
4. 画面上部の「出力方式」を開き、「USB出力（SparkFun Pro Micro）」を選ぶ。
5. 候補を選び、「接続を確認して保存」を押す。接続確認が失敗した場合は保存されない。
6. 表示が「保存済み」なら、常駐を正常終了して再起動する。出力方式は実行中に切り替わらず、次のresident sessionから有効になる。
7. 「使用中: USB出力（Serial HID）」を確認してから使う。

開発workspaceからの起動例:

```powershell
dotnet run --project src/OpenLogicool.Host/OpenLogicool.Host.csproj -- ui --resident
```

Serial HID v1は通常キー同時6個までである。relative pointer／wheelはfirmware 1.1.0以降の`MOUSE_DELTA`で扱う。firmware 1.1.3はCDC応答停止からのHello回復に加え、fail-closed releaseが1秒継続した場合と、USB／CDC処理からmain loopへ2秒戻らない場合にwatchdogでUSBを自己再列挙する。actionは自動再送しない。7個以上の同時押し、音量などのconsumer controlには対応しない。対応外の割り当ては部分送出せず、明示faultで停止する。

## 正常終了

Input Studioを閉じると、fast pathの所有出力を解放し、Serial HIDへ`ALL_UP`を送り、ACK後にserial transportを閉じる。G600を管理している場合は、起動時に適用したlegacy出力抑止を保存済みbaselineへ戻す。

終了時にG600復元エラーが表示された場合は、LGS／G HUB／Options+が停止していることを確認して次を実行する。

```powershell
dotnet run --project src/OpenLogicool.Host/OpenLogicool.Host.csproj -- leftover restore
```

baselineが無い、またはbyte一致を確認できない場合は成功扱いにしない。`probe g600-restore-retry`の既存復旧手順へ戻る。

## fault・抜線・hard kill

- Pro Microが未接続、複数候補、firmware／protocol不一致、ACK timeout、破損frame、sequence不一致になった場合はterminal faultで停止する。SendInputへ自動fallbackしない。
- Pro Microを再接続しても同じsessionは再開しない。Input Studioを終了し、候補が1台だけ応答することを確認して明示的に再起動する。
- hostがhard killされてもfirmware leaseが保持中出力を解放する。実機ではkill要求から148.6321msでkey-upを観測し、250ms予算内だった。
- G600のlegacy抑止がhard kill後に残った場合は、共存ソフトを停止して`leftover restore`を実行する。

## firmware再flash

repo内のfirmware 1.1.3を再flashする場合:

```powershell
pwsh.exe -NoLogo -NoProfile -File scripts/build-serial-hid.ps1
pwsh.exe -NoLogo -NoProfile -File scripts/flash-serial-hid.ps1 -ExpectedDeviceInstanceId 'USB\VID_1B4F&PID_9206\HIDFG'
```

flash scriptはexact target identity、固定toolchain、upload verify、CDC＋keyboard＋mouseの再列挙を検証する。自動bootloader捕捉が失敗した場合だけ、Pro Microをdouble-resetしてCaterina bootloaderを開く。targetが一意に決まらない状態ではflashしない。

以前の第三者firmwareへ戻すには、そのfirmwareの保持済みsketchまたはhexが別途必要である。OpenLogicool repoには第三者firmwareを同梱しない。

## 確認済み範囲

| 項目 | 判定 | 条件 |
|---|---|---|
| firmware build／flash／再列挙 | 確認済み | 固定toolchain、Pro Micro 5V / 16MHz |
| G13／G600 key・mouse button・chord・finite sequence | 確認済み | 同一Serial HID経路 |
| relative pointer／wheel | 確認済み | firmware 1.1.3、Windows hookでnon-injected move／wheelを観測 |
| CDC session回復 | 確認済み | firmware 1.1.3、Hello 2秒／通常action 80ms、releasePending 1秒またはmain loop block 2秒でwatchdog自己再列挙 |
| layer／profile／app-first切替／保存後再起動 | 確認済み | FOX reference machine |
| handled stop／hard kill release | 確認済み | 250ms以内 |
| dispatch latency | 確認済み | 200 edge、p99 3.425ms、max 12.902ms |
| drop／wrong release／stuck | 確認済み | 0／0／0 |
| NIKKEのG13 G1→Esc | 確認済み | 1回押下に1回反応した単一観測だけ |
| Windows低レベルhookでのNIKKE前面中Esc | 未確認 | hookは未観測。ACKやgame反応と混同しない |
| NIKKE前面中F13／wheelの管理者hook受信 | 確認済み | 完全順序、全event `IsInjected=false`、injected 0 |
| 他game／他anti-cheat／長時間運用 | 未確認 | 一般対応を名乗らない |
| Windows 10／ARM64／別board | 未確認 | reference machine外 |
| raw USB report byteの独立capture | 未確認 | ACK／Windows HID観測で代用しない |
| NKRO、consumer control | 非対応 | Serial HID v1の固定境界 |
| LCD、LGS applet、power mode | 未確認 | 本campaignの対象外 |

製品全体の公開claimは引き続き`Partial LGS Replacement`である。Serial HIDの成立を、LGS全機能parity、全game対応、または利用規約上の許可へ拡張しない。
