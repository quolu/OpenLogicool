# Nanoの無入力時lease失効

2026-10-09、Windows native。Bot停止時の記録はHeartbeat sequence 4033へのSequenceViolation。元の停止時の心拍間隔は記録されていないため、その時のWindows遅延量は未確認。

## 再現と修理

製品の`serial-hid-test --idle-ms 200`を追加し、全解放した同じ接続の通信を200ms空けた。導入済みfirmwareは次のHeartbeat sequence 3へ同じSequenceViolationを返した。入力はALL_UPだけで、ゲーム操作は送っていない。`before-idle200.json`を参照。

firmwareは何も保持していない場合も150msで接続・連番を消していた。保持中のkey・modifier・mouse buttonがある場合だけ150msで全解放とprotocol無効化を行い、全解放済みなら接続・連番を保持する修理を実装した。入力の自動再送、別経路への切替、保持中の期限延長は行わない。

配布sketchそのものをfake時計・serial・HID境界で実行するnative試験で、無入力200ms／1秒、modifier・key・6KRO・mouse buttonの150ms全解放、期限切れsessionの拒否、不正連番、USB再接続、入力再送なしを確認した。8 golden vectorと既存のlease・recovery試験も成功。

Hostの診断入口、故障済みprotocolの終了時の再送禁止、次回fault時の前回正常応答からの間隔表示を追加した。関連Host25件と最終22プロジェクト1,652件成功。AVR固定toolchainによるbuild成功。実機への導入と修理後の再現試験は後続の記録へ追記する。

修理中の旧版BotでHeartbeat sequence 3230のTimeoutも発生した。SequenceViolationとは区別し、通信境界の確認を継続する。無入力の再現欠陥を修理したことだけで、すべての通信停止の原因を解明済みとはしない。
