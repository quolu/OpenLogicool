# t02 firmware evidence

- Date: 2026-08-23
- Task: `t02-firmware`
- Evidence level: compile確認済み。実機USB／HID受理はt07まで未確認。

## 実装

- CDC serial＋独自USB HID keyboard／mouse descriptor
- protocol v1のbounded partial reader、CRC-16/CCITT-FALSE、kind／length／sequence／payload検証
- HELLO→READY、SET_STATE／ALL_UP／HEARTBEAT→ACK、失敗→FAULT
- SET_STATEの完全snapshot検証と、両HID report成功後だけのACK
- boot、USB再列挙、HELLO reset、150ms lease expiryのall-up
- HID失敗時のall-up、`InternalFault`、session停止
- all-up失敗をprotocol stateと独立した`releasePending`として保持し、成功まで再送
- Arduino CLI／SparkFun AVR／Arduino AVRの固定build script

## Compile

Command:

```powershell
./scripts/build-serial-hid.ps1
```

Result:

- Arduino CLI 1.5.1
- SparkFun AVR Boards 1.1.13
- Arduino AVR Boards 1.8.8
- FQBN `SparkFun:avr:promicro:cpu=16MHzatmega32U4`
- flash 6072 / 28672 byte（21%）
- global RAM 256 / 2560 byte（10%）
- ELF内のproduct string `OpenLogicool Serial HID`一致
- exit 0

Arduino AVR core自身の`new.cpp`にunused parameter warningが4件ある。firmware sourceのwarningではなく、compileは成功している。

## 境界

このTaskではflashしていない。USB product string、descriptor、CDC/HID同時列挙、report ID、keyboard／mouse受理、leaseの実時間releaseは確認済みと表示しない。t07で各層を別々に実測する。

Arduino AVR 1.8.8の`HID().SendReport`はendpoint送出不能時にblocking待機する。host hard-kill後250ms以内releaseはこのcompile証跡から推定せず、t09の物理観測で判定する。
