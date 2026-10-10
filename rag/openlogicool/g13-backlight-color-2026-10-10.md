# G13 のバックライト色を変える命令（公開実装 libg13）

- 取得日: 2026-10-10
- 出典: ecraven/g13（libg13）の `g13.cc` にある `G13_Device::set_key_color` と `set_mode_leds`。https://github.com/ecraven/g13
- 確度: 中（公開されている一次コード。Linux の libusb 経由の例で、Windows 標準 HID での成立は未確認）

## 内容

どちらも HID class の SET_REPORT（`bmRequestType` = class・interface 宛て、`bRequest` = 9）を control transfer で送る。データは 5 bytes。

| 用途 | wValue | データ（5 bytes） |
| --- | --- | --- |
| バックライトの色 | `0x307`（feature・report ID 7） | `05 赤 緑 青 00` |
| M1〜M3・MR のランプ | `0x305`（feature・report ID 5） | `05 ランプのbit 00 00 00` |

色のデータの先頭は `05` で、wValue の report ID（7）と一致していない。公開実装はこの形で動いている。

## Windows 標準 HID での見込みと未確認の点

- G13 は Windows で feature report ID 4〜7（最大 258 bytes）を公開している（[LCD の調査](g13-lcd-windows-write-2026-08-23.md)の実機結果）。report ID 7 へは `HidD_SetFeature` で届く見込み。
- `HidD_SetFeature` は buffer の先頭を report ID（7）にする必要がある。公開実装の先頭 `05` をそのまま送ることはできない。先頭が `07` でも firmware が受け付けるかは未確認。
- 色を書くたびに本体の不揮発メモリへ保存するかは未確認。保存する作りなら、音に合わせた連続の書き換えは本体を傷める。抜き差しの後に色が残るかで確かめる。
- 連続で書ける回数（毎秒）と、書いている間に入力 report が欠けないかは未確認。

## 実験

`OpenLogicool.Probe g13-backlight-smoke` で段階を分けて確かめる（読み出しだけ → 色を1回 → 抜き差しで保存の有無 → 連続書き込み）。
