# Nanoの停止条件と修理

出典: 製品の実機診断、配布sketch、固定Arduino coreのCDC.cpp、[Arduino公式source](https://raw.githubusercontent.com/arduino/ArduinoCore-avr/master/cores/arduino/CDC.cpp)、[SparkFunのreset手順](https://learn.sparkfun.com/tutorials/pro-micro--fio-v3-hookup-guide/troubleshooting-and-faq)。取得日: 2026-10-09。確度: 下記の再現条件は確認済み。元のBot停止時の遅延量は未記録。

## 無入力のlease

全解放したfirmwareとの通信を200ms空けるだけで、次のHeartbeatがSequenceViolationになることを実機確認した。接続・連番まで消す処理を、入力保持中の150ms全解放と混同していた。

修正版は全解放済みなら接続を維持し、保持中の期限切れは従来どおり全解放して旧sessionを拒否する。配布sketchそのもののfake時計試験で確認済み。実機導入後の同じ再現試験は基板の手動reset待ちであり、未確認。

## 受信済みACK

hostの読取りを100ms遅らせると、正常ACK10バイトが受信済みでも、期限だけを見て捨ててTimeoutになった。transportで期限時点の受信済みバイトだけを読み、完成した正しい応答を受け付ける修理を実装。期限後に不足バイトを待たず、後着データで待機を延長しない。同じ実機の100ms遅延5回成功、開発版へ導入、Host/Input DLL一致、Botの入力と進行を確認した。

## 旧firmwareの書込み

Arduino coreは1200-baud closeでboot keyを置き、120ms watchdog resetを要求する。旧sketchがloopごとにwatchdogを給餌していたため、そのresetを妨げていた。修正版はこの状態で給餌しない。一次資料の短い抜粋は[raw](raw/nano-boot-reset-primary-20261009.md)。

WindowsのHID停止は、Nano一台に限定しても即時停止にならず再起動予約となった。Enable-PnpDeviceで取り消し、ConfigFlags=0・ProblemCode=0を確認した。この経路はflash scriptから撤去した。

正規の`-WaitForReset`は同じ物理USB接続のbootloaderだけを待つ。SparkFunのdouble-reset手順で書込みモードになった後、公式Arduino CLIでverify付きuploadする。USB抜き挿しやPC再起動は必要としない。受入結果と現在地は[実測記録](../../evidence/nano-idle-lease-20261009/acceptance.md)と構造化した引き継ぎ状態を読む。

## 書込み機器の識別

2026-10-10、通常動作時のVID 1B4F / PID 9206と同じLocationPathsに、Arduino Leonardo（VID 2341 / PID 0036、COM5）のWindows列挙履歴があった。通常動作時のUSB名はsketch由来であり、実機のbootloaderがSparkFun純正である根拠にはならない。導入済みのSparkFun bootloader sourceも、実機のbootloader sourceそのものを証明しない。

PID 9205だけに限定していた待機処理は、この正規AVR109機器を取りこぼす。Arduino LeonardoとSparkFun Pro Microの固定boards.txtはともにavr109 / 57600を指定する。物理接続口を維持したまま対応する識別子を追加し、実録を使った選択・非接続履歴・別の接続口・未知機器・複数候補・通常動作中・COM番号未確定の8件を確認した。今回の利用者操作後の新規arrivalは未確認で、履歴の発見を今回のreset成功とは扱わない。
