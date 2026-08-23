# ADR: Serial HID flash and direct hardware smoke

- Date: 2026-08-23
- Status: accepted
- Scope: `t07-flash-and-direct-smoke`

## Decision

- Flash targetはpresentなSparkFun composite device instance ID `USB\VID_1B4F&PID_9206\HIDFG`で一意に限定する。bootloader／runtimeのCOM番号は一時transportとしてだけ使い、identityとして保存しない。
- Firmwareは固定FQBN `SparkFun:avr:promicro:cpu=16MHzatmega32U4`でbuildし、upload verify後に同じContainerId配下のCDC、keyboard、mouse再列挙を確認する。対象が0台／複数台、board identity不一致、再列挙不成立なら停止する。
- Direct smokeは製品の`SerialHidProtocolSession`と`SerialHidEmitter`を通し、probe固有コードは実CDC frame exchangeとWindows観測だけを所有する。SendInputへのfallbackは持たない。
- Windows観測はlow-level keyboard／mouse hookのinjected flagを除外し、key、chord、mouse button、finite sequence、ALL_UP、lease releaseを別々に判定する。
- Power cycleはcomposite device instance IDのPnP presenceで抜線／再接続を判定する。再接続後の予期しないdownが0件であることをall-up成立条件にし、COM番号の存否を判定へ使わない。

## Reason

Pro Microはflashと再列挙でCOM番号が変わり得る。保存identityをCOM番号にすると同じ物理boardの再接続で誤選択するため、安定したPnP identityとContainerIdを基準にする。direct smokeは既存の製品protocol／emitterをそのまま通すことで、probe専用の類似実装がgreenになるだけのfalse successを避ける。

## Verification

- Firmware compile: 6080 / 28672 byte、global 256 / 2560 byte
- Flash: upload verify成功、hex SHA-256 `bb40946e827a04d5c75cc28b442a6ec5a994dcdc48ee5e7eed17b9c560e288c8`
- Enumeration: 同じcomposite配下のCDC、keyboard、mouseがすべてpresent
- HELLO／READY: firmware `1.0.0`、capabilities `0x0007`、lease 150ms
- Direct HID: key、chord、middle mouse、finite sequence、ALL_UPをWindowsの非injected eventとして観測
- Lease: releaseを149.465msで観測
- Power cycle: disconnect／reconnect／all-up成立、unexpected down 0
- Post-cycle: PnP identityから一時portを再解決し、HELLO／READYとdirect HIDを再度確認

## Evidence boundary

確認済みなのはflash／再列挙、CDC serial writeとmatching ACK、Windows low-level hookでの非injected HID event、lease release、power-cycle all-upである。raw USB report byteの独立capture、特定foreground app受理、対象game内成功は本Taskでは未確認であり、後続Taskの証拠へ分離する。
