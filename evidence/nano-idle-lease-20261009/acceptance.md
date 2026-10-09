# Nanoの無入力時lease失効

2026-10-09、Windows native。Bot停止時の記録はHeartbeat sequence 4033へのSequenceViolation。元の停止時の心拍間隔は記録されていないため、その時のWindows遅延量は未確認。

## 再現と修理

製品の`serial-hid-test --idle-ms 200`を追加し、全解放した同じ接続の通信を200ms空けた。導入済みfirmwareは次のHeartbeat sequence 3へ同じSequenceViolationを返した。入力はALL_UPだけで、ゲーム操作は送っていない。`before-idle200.json`を参照。

firmwareは何も保持していない場合も150msで接続・連番を消していた。保持中のkey・modifier・mouse buttonがある場合だけ150msで全解放とprotocol無効化を行い、全解放済みなら接続・連番を保持する修理を実装した。入力の自動再送、別経路への切替、保持中の期限延長は行わない。

配布sketchそのものをfake時計・serial・HID境界で実行するnative試験で、無入力200ms／1秒、modifier・key・6KRO・mouse buttonの150ms全解放、期限切れsessionの拒否、不正連番、USB再接続、入力再送なしを確認した。8 golden vectorと既存のlease・recovery試験も成功。

Hostの診断入口、故障済みprotocolの終了時の再送禁止、次回fault時の前回正常応答からの間隔表示を追加した。関連Host25件と最終22プロジェクト1,652件成功。AVR固定toolchainによるbuild成功。実機への導入と修理後の再現試験は後続の記録へ追記する。

修理中の旧版BotでHeartbeat sequence 3230のTimeoutも発生した。SequenceViolationとは区別し、通信境界の確認を継続する。無入力の再現欠陥を修理したことだけで、すべての通信停止の原因を解明済みとはしない。

## 受信済み応答の時間切れ

`serial-hid-test --read-pause-ms 100`でhost読取りを100ms遅らせたところ、旧読取り処理は正常ACKの10バイトが受信済みでも読まずにTimeoutを返した。`before-read-pause100.json`に受信済み10バイトの実測を保存した。

期限時点の受信済みバイトだけを読み、相関・CRCが正しい完成frameを受け付ける修理をtransportへ実装した。不完全なframeに追加待機を与えず、後着バイトで待機を延長しない。focused 3件と実機の同じ100ms遅延5回が成功（`after-read-pause100.json`）。最終22プロジェクト1,655件成功。

## 書込み待ちとWindows状態

旧firmwareのwatchdogが1200-baud close後も給餌されるため、標準uploadはbootloaderへ移れず失敗した。修正版は1200-baud close中に給餌を止める。sketchそのもののnative試験とAVR buildで確認済み。

NanoのHIDだけを一時停止する試行はWindowsの再起動予約になり、即時停止できなかった。予約を取り消してConfigFlags=0・ProblemCode=0・Status=OKを確認した。この方式は正規scriptから撤去した。PC再起動・USB抜き挿しは行っていない。

K-3UKN4Dへ基板の手動double-resetを依頼し、`-WaitForReset`で同じ物理USB接続のbootloaderを待つ。firmwareの実機導入と修理後のidle200ms／1000ms試験は手動操作後の工程であり、まだ完了として扱わない。元のBot停止時の遅延量が未記録であることと、二つの実機再現条件が確認済みであることを区別する。
