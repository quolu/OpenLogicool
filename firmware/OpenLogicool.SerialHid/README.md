# OpenLogicool Serial HID firmware

SparkFun Pro Micro（ATmega32U4、5V / 16 MHz）をCDC serialとUSB HID keyboard／mouseのbridgeとして動かすfirmwareである。button、relative pointer／wheelに加え、fail-closed release中もserialを先に処理し、Helloを明示回復要求として扱う。releaseが1秒継続した時に加え、USB／CDC処理からmain loopへ2秒戻らない時もhardware watchdogでUSBを自己再列挙する。入力保持中の150ms全解放を維持し、全解放済みの無入力区間では接続を維持する。hostとのwire契約は[protocol-v1.md](protocol-v1.md)を正とする。firmwareの版はsketchのversion tripletを参照する。

Windows PowerShellから次を実行する。

```powershell
./scripts/build-serial-hid.ps1
```

scriptはArduino CLI 1.5.1、SparkFun AVR Boards 1.1.13、Arduino AVR Boards 1.8.8を固定し、downloadとbuildを`%LOCALAPPDATA%\OpenLogicool\Arduino*`だけへ置く。生成したhexはrepoへ追加しない。flashは実機受入Taskで別に行う。

USB product stringは`OpenLogicool Serial HID`、manufacturer stringは`OpenLogicool`でbuildする。SparkFun Pro Micro固有のVID／PIDはboard定義の`1B4F:9206`を維持し、Logicool製品や一般keyboardへ偽装しない。
