# ADR: Serial HID Output campaignのExit

- 日付: 2026-08-23
- 状態: Accepted
- 計画: [Serial HID Output campaign](../serial-hid-output-campaign-plan.md)
- 判定表: [Serial HID Output campaign Exit Assessment](../serial-hid-output-exit-assessment.md)
- 終端証拠: [t10 campaign終端受入](../../evidence/serial-hid-output/t10-campaign-exit.md)
- 独立監査: [Fable read-only反証監査](../../evidence/serial-hid-output/t10-fable-exit-audit.md)

## 決定

Serial HID Output campaignはExit 11条件をすべて満たしたため`CLOSE`とする。

Serial HID v1は、SparkFun Pro Micro ATmega32U4 5V / 16MHz、firmware 1.0.0、Windows 11 x64、6KRO以内、通常keyboard usageとmouse buttonというreference条件でSupportedとする。G13／G600は同じSerial HID経路で動作し、未接続・故障時にSendInputへfallbackしない。

製品全体の公開claimは`Partial LGS Replacement`のまま維持する。NIKKEは2026-08-23のG13 G1→Esc単一押下で、ACK down／up各1回とgame内反応1回だけを確認済みとする。Windows低レベルhook未観測、他game、長時間運用、Windows 10、ARM64、別board、LCDは未確認のまま昇格しない。

## 独立監査

Peertable監査席が無応答だったため、オーナー指示により別sessionのFable 5によるread-only反証監査で代替した。同じmodel系列でcross-providerではない制約を明記した上で、計画が要求する独立監査1回として採用する。

監査が検出したAGENTS現在地未反映、未追跡probeの仕分け漏れ、経路非中立の診断文はすべて終了前に修理した。最終sourceは20 test project、829件、failed 0、skipped 0である。

## 運用

LGSは将来のLCD利用に備えてinstall済みのまま保持できる。ただしSerial HID運用中は自動起動を無効にし、`LCore.exe`を停止する。通常操作、故障、復旧、firmware再flashは[運用手順](../serial-hid-output-operation.md)を正とする。
