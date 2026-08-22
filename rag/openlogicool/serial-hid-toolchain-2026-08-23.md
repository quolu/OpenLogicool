# Serial HID firmware toolchain調査

- 取得日: 2026-08-23
- 確度: 高（公式release、公式package index、導入後core source、固定compileの突合）

## 結論

OpenLogicool Serial HID firmwareは次の組合せへ固定する。

| 要素 | 固定版 | 一次資料／checksum |
|---|---:|---|
| Arduino CLI | 1.5.1 | [公式release](https://github.com/arduino/arduino-cli/releases/tag/v1.5.1)、Windows 64-bit zip SHA-256 `fabe42e0eb04d00e776a66178299ff95a46c623dbc260f997e58fd514853dd40` |
| SparkFun AVR Boards | 1.1.13 | [SparkFun公式package index](https://raw.githubusercontent.com/sparkfun/Arduino_Boards/main/IDE_Board_Manager/package_sparkfun_index.json)、SHA-256 `D7AF391CAFC5E16830CAC7C13484EF62765DD7A36AABA5F25020CE3C39617115` |
| Arduino AVR Boards | 1.8.8 | [Arduino公式package index](https://downloads.arduino.cc/packages/package_index.json)、SHA-256 `a234c2a43dcd01ce54be665806f183b8e6ec4e966d2e3e0c3358b63023d6390c` |
| AVR GCC | 7.3.0-atmel3.6.1-arduino7 | [Arduino公式package index](https://downloads.arduino.cc/packages/package_index.json)、Windows archive SHA-256 `a54f64755fff4cb792a1495e5defdd789902a2a3503982e81b898299cf39800e` |

Arduino CLI 1.5.1は2026-08-23時点のlatest stableで、1.5.2-rc.1はpre-releaseのため採らない。SparkFun Pro Micro定義は`SparkFun:avr:promicro`、5V / 16 MHz optionは`cpu=16MHzatmega32U4`であり、VID/PID `1B4F:9206`、MCU `atmega32u4`、flash上限28672 byteである。

SparkFun 1.1.13の`boards.txt`はPro Microのcoreを`arduino:arduino`として参照する。したがってSparkFun packageだけでは再現可能buildにならず、Arduino AVR Boardsも現行stable 1.8.8へ固定する必要がある。

build scriptはArduino CLIにpackage archiveの公式checksum検証を任せるだけでなく、download cacheのSparkFun／Arduino AVR／AVR GCC archiveを同じSHA-256へ毎回再照合する。さらに実行する`arduino-cli.exe`と`avr-g++.exe`のSHA-256も固定し、version文字列だけで固定版を判定しない。

## HID APIの確認

Arduino AVR 1.8.8のHID libraryは`HIDSubDescriptor`、`HID().AppendDescriptor(...)`、`HID().SendReport(reportId, data, length)`を提供する。[ArduinoCore-avrのHID source](https://github.com/arduino/ArduinoCore-avr/tree/1.8.8/libraries/HID)と導入後sourceを突合した。`SendReport`はreport IDを先頭に付けるため、firmware descriptorと送出側でreport IDを一か所ずつ明示する。

SparkFun board定義は`USB_PRODUCT`と`USB_MANUFACTURER`をbuild propertyからC文字列macroへ渡す。build scriptは空白をCのoctal escape `\040`で表してargument分割を避け、実際のUSB product stringを`OpenLogicool Serial HID`にする。実device descriptorでの最終確認はflash後のt07で行う。

## 実測

`scripts/build-serial-hid.ps1`をWindows PowerShellで実行し、次を確認した。

- Arduino CLI: `Version: 1.5.1 Commit: 01f3d4f2b`
- installed platform: `SparkFun:avr 1.1.13`、`arduino:avr 1.8.8`
- FQBN: `SparkFun:avr:promicro:cpu=16MHzatmega32U4`
- compile: green
- flash: 6072 / 28672 byte（21%）
- global RAM: 256 / 2560 byte（10%）
- ELF内のproduct string: ASCII `OpenLogicool Serial HID`一致

初回のArduino AVR core配置を対話PTYから行うと、古いWindows driver用`post_install.bat`が無人で停止した。firmware compileにdriver installは不要であり、固定scriptは公式の`--skip-post-install`を明示する。既存のArduino環境へは入れず、toolchain、package data、build artifactを`%LOCALAPPDATA%\OpenLogicool\Arduino*`へ隔離する。
