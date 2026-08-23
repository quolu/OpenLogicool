# ADR: G13 LCD standard HID transport

- Date: 2026-08-23
- Status: accepted
- Scope: `p1-standard-hid-write-gate`

## Decision

- G13 LCDのWindows transportは、標準HidUsbが公開する992-byte output collectionをread/writeで開き、`WriteFile`で完成frameを送る方式だけを採用する。
- wire reportはreport ID `0x03`をbyte 0に置き、byte 1〜31をzero header、byte 32〜991を160×43 monochrome framebufferとする。
- `HidD_SetOutputReport`は採用しない。WinUSB、libusb、driver差替えにも進まない。
- LCD I/OはG13 input fast pathから独立させる。表示更新のためにRaw Input処理を待たせない。

## Reason

実機の単一HID collectionはinput 8 bytes、output 992 bytes、feature最大258 bytesを公開した。
solid frameを`WriteFile`へ渡すと992/992 bytesで完了し、LCDが大きな「G13」表示から白一色へ変化した。
その後も同じcollectionからG1 down/upをsequence 1/2で取得し、drop 0だった。

診断限定で試した`HidD_SetOutputReport`はWin32 error 31で失敗した。
標準HidUsbのまま表示と入力継続が成立したため、driver差替えの必要性はない。

## Verification

- Devices.G13 focused test: 21件成功、失敗0
- Probe build: 警告0、エラー0
- LCD write: `WriteFile` 992/992 bytes
- LCD目視: solid frameにより白一色へ変化
- post-write input: G1 down→up、sequence 1/2、drop 0
- 証跡: `evidence/g13-native-lcd/p1-standard-hid-write-gate.md`

## Evidence boundary

確認済みなのは一枚の揮発性frame送信、LCD反映、write後の入力継続である。
resident hostでの継続更新、app／workspace／profile表示、hotplug再表示、更新中のlatencyはPhase 2で確認する。
最初のsparse patternが見えなかった原因は未測定であり、本Decisionでは断定しない。
