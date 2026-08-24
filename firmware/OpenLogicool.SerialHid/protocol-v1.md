# OpenLogicool Serial HID protocol v1

この文書はWindows hostとATmega32U4 firmwareが共有するbyte-level契約である。整数はすべてunsigned little-endian。frameをUSB HIDへ適用できるのは、magic、version、kind、length、CRC、payload検証がすべて通った後だけとする。

## Frame

| offset | 幅 | field | v1 |
|---:|---:|---|---|
| 0 | 2 | magic | `4F 4C` (`OL`) |
| 2 | 1 | protocol version | `01` |
| 3 | 1 | message kind | 下表 |
| 4 | 2 | sequence | little-endian `uint16` |
| 6 | 2 | payload length | little-endian `uint16`、0〜32 |
| 8 | N | payload | kind別固定長 |
| 8+N | 2 | CRC | little-endian `uint16` |

CRCはCRC-16/CCITT-FALSE（poly `0x1021`、init `0xFFFF`、refin/refout false、xorout `0x0000`）。magic先頭からpayload末尾までを対象とし、CRC自身は含めない。標準check `123456789` は `0x29B1`。

readerは任意個のpartial readをbufferし、`4F 4C`まで破棄して再同期する。header受信後に宣言長が32を超えたframeは適用せず、既知sequenceで`LengthMismatch`を返して再同期する。CRC不一致frameも適用しない。相関不能なgarbage／magic不一致はsequence `0`のFAULTを送るか無応答で再同期できるが、HID stateは変更してはならない。

host request sequenceは`1..65535`。`65535`の次は`1`であり、`0`は相関不能FAULT専用。hostは同時に未決requestを1件だけ持つ。READY／ACK／FAULTは対象requestと同じsequenceをframe headerへ入れる。sequence不一致応答は成立扱いせずhost faultとし、自動再送しない。

## Message kind

| 値 | kind | payload |
|---:|---|---|
| `01` | HELLO | host version 3 byte＋requested capability `uint16`（5 byte） |
| `02` | READY | firmware version 3 byte＋protocol version 1 byte＋capability `uint16`＋max normal keys 1 byte＋lease ms `uint16`（9 byte） |
| `03` | SET_STATE | 完全HID state（8 byte） |
| `04` | ALL_UP | なし |
| `05` | HEARTBEAT | なし |
| `06` | ACK | なし |
| `07` | FAULT | fault code 1 byte＋offending kind 1 byte |
| `08` | MOUSE_DELTA | relative X 1 byte＋relative Y 1 byte＋wheel 1 byte |

version tripletはmajor、minor、patchを各`uint8`で表す。capability bitは`0x0001=Keyboard6Kro`、`0x0002=MouseButtons`、`0x0004=LeaseRelease`、`0x0008=RelativeMouse`。未知bitを要求されたfirmwareはREADYで黙って落とさず`UnsupportedCapability`を返す。READYのcapabilityはfirmwareが持つ全bitではなく、HELLOで要求され成立したsubsetだけを返す。これにより旧hostが新firmwareの未知bitを受け取らない。v1 READYはprotocol version `01`、max normal keys `06`、初期lease `150`を返す。

## SET_STATE payload

| offset | 幅 | field |
|---:|---:|---|
| 0 | 1 | USB keyboard modifier mask |
| 1 | 6 | 通常keyboard usage、数値昇順・重複なし・末尾zero padding |
| 7 | 1 | mouse button mask (`bit0..4 = Left, Right, Middle, X1, X2`) |

payloadはedgeでなく現在押下中の完全stateである。通常key slotは`0x04..0xDF`だけを受理し、`0x01..0x03`のrollover code、`0xE0..0xE7`のmodifier usage、`0xE8..0xFF`の予約usageを拒否する。modifierはoffset 0だけへ置く。通常keyの7個目を切り捨て、rollover errorに変換、先頭6個だけ送出してはならない。hostはframeを作る前に全checkpointを拒否する。mouse maskのbit5〜7は0固定。

firmwareは検証済みSET_STATE全体をkeyboard reportとmouse button reportへ適用した後だけACKする。適用途中のreportをACKしてはならない。ALL_UPは両reportをall-upへ適用した後だけACKする。HEARTBEATのACKはlease更新成立を表す。

## MOUSE_DELTA payload

| offset | 幅 | field |
|---:|---:|---|
| 0 | 1 | relative X、two's-complement `int8` |
| 1 | 1 | relative Y、two's-complement `int8` |
| 2 | 1 | wheel、two's-complement `int8` |

3 fieldはHID descriptorのlogical rangeに合わせてそれぞれ`-127..127`とし、`0x80`（`-128`）を拒否する。MOUSE_DELTAは保持stateではない。firmwareは最後に成立したSET_STATEのmouse button maskを保持し、そのmaskとdeltaを一つのmouse reportとして送る。keyboard reportは送らない。button maskの書き手はSET_STATE／ALL_UP／lease releaseだけであり、MOUSE_DELTA payloadへ重複させない。

MOUSE_DELTAは検証済みかつexpected sequenceの時だけ1回適用し、sequenceとleaseを更新してからACKする。hostはACK timeout、FAULT、破損応答で同じdeltaを再送しない。ACK喪失時は適用0回または1回の`outcome unknown`であり、再接続後にcursorと画面を再観測するまで続行しない。SendInputや別deviceへfallbackしない。

## Fault code

| 値 | code | 意味 |
|---:|---|---|
| `01` | BadMagic | decoderへ渡されたframeのmagic不一致 |
| `02` | UnsupportedVersion | protocol version不一致 |
| `03` | ChecksumMismatch | CRC不一致 |
| `04` | LengthMismatch | 宣言長、受信長、上限の不一致 |
| `05` | UnknownMessage | 未知message kind |
| `06` | InvalidPayload | kind別固定長・予約bit・key表現の違反 |
| `07` | UnsupportedCapability | HELLO要求capabilityを満たせない |
| `08` | SequenceViolation | firmwareが受理できないsequence関係 |
| `09` | InternalFault | firmware内部でHID適用を完了できない |

FAULTは失敗したrequestと同じsequenceを使い、HID stateを変更しない。hostはFAULT、ACK timeout、sequence不一致、破損応答を明示faultにして停止し、同じ要求を自動再送せず、SendInputへfallbackしない。

## Stateとlease

hostはresolved outputごとの参照数を持ち、同じkeyをG13とG600が保持しても片方のupだけではsnapshotから外さない。down群またはup群をtentative stateへ適用し、SET_STATEのACK後だけcommitted stateへ進める。対応downのないup、変換不能usage、6KRO超過は送信前faultである。

Game Operatorがrelative pointerを使うsessionはHELLOで`0x000F`を要求する。keyboard／buttonだけの既存sessionは`0x0007`を要求できる。旧firmwareは`0x000F`を`UnsupportedCapability`で拒否し、hostは再flashが必要な明示faultとして止める。

chordの同方向edge群は1 snapshot。有限sequenceはdown群とup群をcheckpointに分け、各checkpointを順番にSET_STATE→ACK→commitする。keyboardとmouseが同じcheckpointにあれば1 payloadで確定する。

firmwareはboot、USB再列挙、protocol resetをall-upから始め、最終有効frameから150msでlease切れにしてkeyboard／mouseをall-upにする。hostは50ms間隔でHEARTBEATする。handled stopは新規down停止→所有output release→ALL_UP ACK→serial close。Windows watchdogはSerial HIDのrelease所有者ではない。

## Golden vectors

同じdirectoryの`protocol-v1-golden-vectors.json`をC# codec testとfirmware decoder testが共有する。hexは空白区切りで、CRC byteを含む完成frameである。vectorを変更する場合はprotocol versionを据え置いて互換性を壊してはならない。
