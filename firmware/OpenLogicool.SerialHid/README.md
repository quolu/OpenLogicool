# OpenLogicool Serial HID firmware

SparkFun Pro Micro（ATmega32U4、5V / 16 MHz）をCDC serialとUSB HID keyboard／mouseのbridgeとして動かすfirmwareである。hostとのwire契約は[protocol-v1.md](protocol-v1.md)を正とする。

Windows PowerShellから次を実行する。

```powershell
./scripts/build-serial-hid.ps1
```

scriptはArduino CLI 1.5.1、SparkFun AVR Boards 1.1.13、Arduino AVR Boards 1.8.8を固定し、downloadとbuildを`%LOCALAPPDATA%\OpenLogicool\Arduino*`だけへ置く。生成したhexはrepoへ追加しない。flashは実機受入Taskで別に行う。

USB product stringは`OpenLogicool Serial HID`、manufacturer stringは`OpenLogicool`でbuildする。SparkFun Pro Micro固有のVID／PIDはboard定義の`1B4F:9206`を維持し、Logicool製品や一般keyboardへ偽装しない。
