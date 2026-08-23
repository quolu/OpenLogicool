# G13 LCD を Windows 標準 HID から書くための調査

- 取得日: 2026-08-23
- 対象: Logicool G13（VID 046D / PID C21C）の 160×43 monochrome LCD
- 確度: 高（Microsoft 公式仕様、G13 公開実装の一次コード、Windows実機caps／write実測）

## 結論

最初に試した経路は、Windows の標準 HID device interface を `CreateFile` で開き、
`HidP_GetCaps` の `OutputReportByteLength` が 992 bytes の top-level collection に
`WriteFile` で一枚の output report を送る方式である。

`HidD_SetOutputReport` は使わない。Microsoft は連続 output report に `WriteFile` を使うよう示し、
`HidD_SetOutputReport` は機器によって無応答になる可能性を明記している。
実機は992-byte output reportを公開し、`WriteFile`で全bitを立てたframeを992 bytes全長送信した後、
オーナー目視でLCDが白一色へ変化した。さらに同じHID collectionからG1 down/upを順番どおり取得し、drop 0を確認した。
したがってG13 LCDのWindows製品経路は、標準HidUsbを維持した`WriteFile`とする。
診断限定の`HidD_SetOutputReport`はWin32 error 31で失敗したため不採用とし、
WinUSB/libusb用driverへの差替えも不要である。

## G13 の framebuffer wire 形状

公開実装 `khampf/g13` の `g13_lcd.cpp` は次を行う。

- 画素データは 960 bytes。
- 送信 buffer は 32-byte header と 960-byte framebuffer の計 992 bytes。
- header はゼロ初期化し、byte 0 だけを `0x03` にする。
- framebuffer は offset 32 から置く。
- USB interrupt OUT endpoint 2へ992 bytesを送る。
- 初期化時は zero-lengthの HID `SET_REPORT` 相当を一度送り、その後にframeを送る。

framebuffer の並びは、横160 columnを1 byteずつ並べ、縦8 pixelを各byteのbit 0〜7に置く。
160×43 pixelなので、6 band（最後の5 pixelを含む）×160 bytes = 960 bytesになる。

公開repositoryには明示licenseが見当たらないため、上記の観測事実だけを採用し、コードは移植しない。

## Windows 側の判断

Microsoft の HIDClass 契約では、user-mode applicationが連続してoutput reportを送る標準経路は`WriteFile`である。
write長は対象top-level collectionの`HIDP_CAPS.OutputReportByteLength`に合わせる必要がある。
したがって、G13で先に確定すべきなのはendpoint番号をuser-modeから直接指定する方法ではなく、
HidUsbが992-byte output reportを公開するcollectionを作っているかどうかである。

この判断から、最小probeは次の順序にする。

1. 現在接続中の全HID interfaceを列挙する。
2. VID/PIDが046D:C21Cのinterfaceだけを開く。
3. usage page、input/output/feature report長を記録する。
4. output長992のinterfaceが一つだけなら、そのhandleへ`WriteFile`で識別しやすい一枚絵を送る。
5. 画面の目視後、既存Raw Input経路でG13の押下／解放が継続することを確認する。

output長992のcollectionが存在しない、複数あって一意に決められない、またはwriteが全長完了しない場合は失敗とする。
別APIやdriver差替えへ黙ってfallbackしない。

## Windows実機結果（2026-08-23）

- G13 firmware release: 0203
- top-level collection: 1件
- usage: `0xFF00:0x0000`
- input: report ID 1、8 bytes
- output: report ID 3、992 bytes
- feature: report ID 4〜7、最大258 bytes
- `WriteFile` sparse pattern: return success、992/992 bytes、即時の目視変化なし
- `WriteFile` solid pattern: return success、992/992 bytes、オーナー目視で「真っ白」への変化を確認
- `HidD_SetOutputReport` solid pattern: Win32 error 31、不成立
- write後のRaw Input: G1 down（sequence 1）→up（sequence 2）、drop 0

最初の線の細いpatternは目視確認できず、その一試行だけを
`evidence/g13-native-lcd/p1-writefile-no-visible-change.md`へ残す。
後続の高コントラストframeでLCD反映を確認したため、transport全体の最終判定は
`evidence/g13-native-lcd/p1-standard-hid-write-gate.md`を正とする。

macOSの別一次実装 `golgote/G13` は同じreportを
`IOHIDDeviceSetReport(..., kIOHIDReportTypeOutput, 0x03, ..., 992)`で送り、事前初期化を置かない。
Windowsで`HidD_SetOutputReport`も診断したが、この実機ではerror 31で失敗した。
製品実装はMicrosoftが連続output report向けに示す`WriteFile`だけを使う。

## 安全境界

- LCD frameは揮発性で、profile／firmware／driver／registryを書き換えない。
- 実験中もLGSは停止したままにし、競合ownerを作らない。
- write失敗後はhandleを閉じる。機器が無応答になった場合の復旧はG13の抜差し。
- 標準入力が失われた場合は不成立とし、同じ経路の製品化へ進まない。

## 一次資料

- Microsoft, “Sending HID Reports”: https://learn.microsoft.com/en-us/windows-hardware/drivers/hid/sending-hid-reports
- Microsoft, “HidD_SetOutputReport function”: https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/hidsdi/nf-hidsdi-hidd_setoutputreport
- Microsoft, “HID Application Programming Interface”: https://learn.microsoft.com/en-us/windows-hardware/drivers/hid/hid-api
- khampf/g13, `g13_lcd.cpp`: https://github.com/khampf/g13/blob/master/g13_lcd.cpp
- khampf/g13, `g13.hpp`: https://github.com/khampf/g13/blob/master/g13.hpp
- golgote/G13, `G13Device.swift`: https://github.com/golgote/G13/blob/main/G13/G13Device.swift
