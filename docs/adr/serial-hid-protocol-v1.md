# Serial HID protocol v1を固定する

日付: 2026-08-23  
状態: 採用

## Decision

Serial HID protocol v1は次で固定する。

- frameは`4F 4C` magic、version `uint8`、kind `uint8`、sequence `uint16`、payload length `uint16`、最大32 byte payload、CRC `uint16`。複数byte整数とCRCの格納はlittle-endian。
- CRCはCRC-16/CCITT-FALSEで、magicからpayload末尾までを覆う。
- request sequenceは1〜65535を一つずつ使い、0を相関不能FAULT専用にする。READY／ACK／FAULTは対象requestと同じsequenceを使う。
- SET_STATEはmodifier 1 byte、通常usage 6 byte、mouse button mask 1 byteの完全snapshot。edge列をwireへ流さない。
- HELLO、READY、SET_STATE、ALL_UP、HEARTBEAT、ACK、FAULTのkindと全payload長、fault codeを`firmware/OpenLogicool.SerialHid/protocol-v1.md`どおり固定する。
- 同方向edge群を1 checkpointにし、resolved outputの参照数からtentative snapshotを作る。matching ACK sequence後だけcommitする。finite sequenceはdown群／up群のcheckpoint順を保持する。
- wrong up、変換不能usage、通常key 7個目は送信前faultであり、部分snapshotを送らない。FAULT、timeout、破損、sequence不一致を自動再送せず、SendInputへfallbackしない。

C#とfirmwareは`firmware/OpenLogicool.SerialHid/protocol-v1-golden-vectors.json`を同じ入力として検証する。互換性を壊すbyte変更はv1を上書きせず新versionにする。

## Reason

ATmega32U4でbounded memoryのdecoderを実装できる固定長中心の契約にしつつ、partial read、破損、旧firmware、ACK取り違えを明示検出する必要がある。raw edgeのfire-and-forgetではpartial chord、重複所有の早期release、host crash後の保持状態を解決できないため採用しない。
