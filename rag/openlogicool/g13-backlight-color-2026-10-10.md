# G13 のバックライト色を変える命令（公開実装 libg13）

- 取得日: 2026-10-10
- 出典: ecraven/g13（libg13）の `g13.cc` にある `G13_Device::set_key_color` と `set_mode_leds`。https://github.com/ecraven/g13
- 確度: 高（公開されている一次コード＋Windows 実機での書き込み・読み出し・連続書き込みの実測）

## 内容

どちらも HID class の SET_REPORT（`bmRequestType` = class・interface 宛て、`bRequest` = 9）を control transfer で送る。データは 5 bytes。

| 用途 | wValue | データ（5 bytes） |
| --- | --- | --- |
| バックライトの色 | `0x307`（feature・report ID 7） | `05 赤 緑 青 00` |
| M1〜M3・MR のランプ | `0x305`（feature・report ID 5） | `05 ランプのbit 00 00 00` |

色のデータの先頭は `05` で、wValue の report ID（7）と一致していない。公開実装はこの形で動いている。

## Windows 標準 HID での見込み（実験前の整理）

- G13 は Windows で feature report ID 4〜7（最大 258 bytes）を公開している（[LCD の調査](g13-lcd-windows-write-2026-08-23.md)の実機結果）。report ID 7 へは `HidD_SetFeature` で届く見込み。
- `HidD_SetFeature` は buffer の先頭を report ID（7）にする必要がある。公開実装の先頭 `05` をそのまま送ることはできない。先頭が `07` でも firmware が受け付けるかは未確認。
- 色を書くたびに本体の不揮発メモリへ保存するかは未確認。保存する作りなら、音に合わせた連続の書き換えは本体を傷める。抜き差しの後に色が残るかで確かめる。
- 連続で書ける回数（毎秒）と、書いている間に入力 report が欠けないかは未確認。

## Windows 実機結果（2026-10-10）

`OpenLogicool.Probe g13-backlight-smoke` で段階を分けて確かめ、すべて成立した。判定と証跡は [evidence/g13-backlight/p1-standard-hid-feature-write-gate.md](../../evidence/g13-backlight/p1-standard-hid-feature-write-gate.md)。

- `HidD_SetFeature` に `07 赤 緑 青 00`（残りは 0 で埋めて collection の feature report 長 258 bytes）を渡すと色が変わる。先頭が `07` でも firmware は受け付ける。
- `HidD_GetFeature`（report ID 7）で今の色を読める。色は 2〜4 byte 目。
- 色は本体に保存されない。抜き挿しで電源投入時の色（`5A FF 6E`）へ戻る。
- 毎秒 30 回の連続書き込みで失敗 0、1回あたり約 0.3 ms。書いている間も G13 の入力は欠けない。
