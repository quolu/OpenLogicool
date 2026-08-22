# ADR: Serial HID cross-language fake gates

- Date: 2026-08-23
- Status: accepted
- Scope: `t06-focused-and-fake-gates`

## Decision

- C# codecとfirmware decoderの一致は、同じ`protocol-v1-golden-vectors.json`を両方へ入力して検証する。firmware側は`ProtocolV1.cpp`をWindows native C++ test executableへ直接linkし、別言語の模擬decoderへ置き換えない。
- firmware leaseの150ms境界は、製品firmwareが直接使うpure `FirmwareLease`をnative testへlinkし、149ms／150msと`millis()`の`uint32` wraparoundをfake clockで確認する。
- CDC serialのpartial readは`SerialHidResponseFrameAssembler`へ分離し、実transportもそのassemblerへ1 byteずつ供給する。garbage後のmagic再同期、任意chunk分割、宣言長超過をHost focused testで確認する。
- checksum／unknown versionはC# codecだけでなく実firmware decoderでも別faultとして検証する。
- 実USB HID、実lease release時間、foreground受理、game内成功はfake gateへ含めない。

## Reason

golden vectorをC#だけで読むテストではfirmware実装との乖離を検出できない。`ProtocolV1.cpp`そのものをhost-nativeで実行すれば、ATmega32U4をflashする前にbyte幅、endianness、CRC、kind、sequence、payloadの一致を検出できる。leaseも同じpure classをfirmwareとtestで共有し、テスト専用の似た計算を作らない。

## Verification

- firmware native: golden vector 7件、checksum fault、version fault、lease 149/150ms＋wraparound成功
- firmware compile: 6080 byte（21%）、global 256 byte（10%）、成功
- Input focused: 141件成功、失敗0
- Host focused: 105件成功、失敗0
- Desktop focused: 77件成功、失敗0
- Architecture focused: 6件成功、失敗0

