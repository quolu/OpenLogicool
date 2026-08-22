# ADR: Serial HID firmware v1

- Status: Accepted
- Date: 2026-08-23
- Scope: `t02-firmware`

## 決定

SparkFun Pro Micro 5V / 16 MHzを、Arduino USB CDCと独自HID report descriptorを持つ複合deviceとしてbuildする。HID report IDはkeyboard=`1`、mouse=`2`とし、keyboard reportはmodifier＋reserved＋6 usage、mouse reportは5 button＋常時zeroのX/Y/wheelである。host protocolの`SET_STATE`は両reportを送る前に全payloadを検証し、両方の送出が成功した後だけACKする。途中失敗はall-upを試行して`InternalFault`を返し、protocol sessionを停止する。

firmwareがreleaseを所有する。最終受理frameから150msでleaseを切り、all-upが成功するまでrelease送出を継続する。boot、USB再列挙、HELLOによるprotocol resetもall-upから開始する。Windows watchdogやSendInputへrelease責務を移さない。

Arduino AVR 1.8.8の`HID().SendReport`は内部でblocking `USB_Send`を使い、endpointが送出不能な場合は各callが最大250ms待つ。host process hard-kill時もWindowsのUSB pollingは継続するため通常は即時送出できる見込みだが、250ms以内releaseはcompileだけでは成立扱いにしない。t07でprotocol／USBの各層、t09でhost hard-killからphysical key-upまでを実測し、満たせなければcore API依存を解消する。150ms lease値は緩めない。

frame readerは最大42 byteの固定bufferだけを持つ。partial readを蓄積し、magicへ再同期し、version、kind、sequence、payload length、CRC、kind別payloadを検証してからHIDへ適用する。host requestは1件ずつで、HELLO後はsequenceの連続性を要求する。

## 理由

raw edgeではchord、finite sequence、duplicate ownershipの確定点を保持できない。完全snapshotとACK後commitなら、hostとfirmwareが同じHID stateを共有できる。keyboardとmouseはUSB上で別reportになるため物理packet単位では同時にならないが、firmwareは全体を先に検証し、部分成功をACKせずall-upへ倒すことでprotocol上の部分commitを作らない。

core既定のKeyboard／Mouse APIはedge単位の中間reportを送るため使わない。Arduino AVR coreの`HID().SendReport`と独自descriptorを使い、各checkpointを完成reportとして送る。

## Toolchain

- Arduino CLI 1.5.1
- SparkFun AVR Boards 1.1.13
- Arduino AVR Boards 1.8.8（SparkFun board定義の`core=arduino:arduino`参照先）
- FQBN `SparkFun:avr:promicro:cpu=16MHzatmega32U4`

固定版、公式URL、checksum、core参照関係は[`rag/openlogicool/serial-hid-toolchain-2026-08-23.md`](../../rag/openlogicool/serial-hid-toolchain-2026-08-23.md)に記録する。

## 未実測境界

compileだけではUSB descriptor、product string、CDC/HID同時列挙、report ID、OS受理、lease releaseを確認済みにしない。これらはt07で実機flash後に別々に受入する。
