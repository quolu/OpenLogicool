# Serial HID Windows discovery調査

- 取得日: 2026-08-23
- 確度: 高（Microsoft公式API資料、SparkFun Boards導入済み定義、.NET公式API資料）

## 結論

CDC serial候補はCOM番号一覧の総当たりではなく、Windowsの`GUID_DEVINTERFACE_COMPORT`（`86E0D1E0-8089-11D0-9CE4-08003E301F73`）をSetupAPIで列挙する。`SetupDiGetClassDevs`と`SetupDiEnumDeviceInterfaces`でpresentなdevice interfaceを取得し、`SetupDiGetDeviceInstanceId`のPnP device instance IDをstableな選択identityとして保存する。その時点のdevice registry `PortName`だけを`SerialPort`の接続先に使う。

SparkFun AVR 1.1.13の導入済み`boards.txt`では、Pro Micro 5V / 16MHzのVIDは`1B4F`、PIDは`9205`／`9206`、runtime build PIDは`9206`である。bootloader／runtime遷移や差し直しでCOM番号が変わり得るため、COM番号は設定identityにしない。VID/PID候補限定の後、OpenLogicool protocol v1のHELLO／READYが成立するかを最終判定にする。

同期transportは`.NET 10`の`System.IO.Ports.SerialPort`を使う。`ReadTimeout`を残り期限へ更新しながらpartial readを完成frameへ組み立て、timeout、transport fault、protocol faultを分離する。

## 一次資料

- Microsoft: GUID_DEVINTERFACE_COMPORT  
  https://learn.microsoft.com/en-us/windows-hardware/drivers/install/guid-devinterface-comport
- Microsoft: SetupDiGetClassDevs  
  https://learn.microsoft.com/en-us/windows/win32/api/setupapi/nf-setupapi-setupdigetclassdevsw
- Microsoft: SetupDiEnumDeviceInterfaces  
  https://learn.microsoft.com/en-us/windows/win32/api/setupapi/nf-setupapi-setupdienumdeviceinterfaces
- Microsoft: SetupDiGetDeviceInstanceId  
  https://learn.microsoft.com/en-us/windows/win32/api/setupapi/nf-setupapi-setupdigetdeviceinstanceidw
- Microsoft: SerialPort.ReadTimeout  
  https://learn.microsoft.com/en-us/dotnet/api/system.io.ports.serialport.readtimeout?view=net-10.0-pp
- NuGet: System.IO.Ports 10.0.11  
  https://www.nuget.org/packages/System.IO.Ports/10.0.11
- ローカル一次資料: SparkFun AVR Boards 1.1.13 `boards.txt`（Pro Micro 5V / 16MHz VID/PID）

