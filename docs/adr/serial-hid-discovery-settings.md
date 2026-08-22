# ADR: Serial HID discovery and machine-local route settings

- Date: 2026-08-23
- Status: accepted
- Scope: `t05-discovery-settings-ui`

## Decision

- WindowsのCDC候補は`GUID_DEVINTERFACE_COMPORT`をSetupAPIで列挙し、SparkFun Pro Microのruntime VID/PID（`VID_1B4F`、`PID_9205`／`PID_9206`）だけへ限定する。
- product設定へ保存するidentityはPnP device instance IDとする。COM番号は列挙時の一時的な接続先としてだけ使い、保存を拒否する。
- 対象を未指定なら候補すべて、指定済みならそのdevice instance IDだけへHELLOを送り、protocol v1 READYが成立した1台だけを採用する。0台、複数台、handshake不成立は別の日本語エラーにする。
- machine-local設定はschema `1.0`のJSONとし、requested route、選択device instance IDを保存する。現在のresidentが使うactive routeとは別に表示し、変更は次回resident起動から反映する。
- Serial HIDを保存する時は接続testを必須にする。失敗時は設定を更新しない。SendInputへの黙ったfallbackは作らない。
- output routeの選択はemitter/sessionの差し替えだけに留め、G13／G600 raw input sourceを追加生成しない。

## Reason

COM番号はflash、bootloader遷移、USB差し直しで変わり得るためidentityにできない。SetupAPIのdevice instance IDで同じ物理候補を再解決し、その時点の`PortName`をtransportに渡す。VID/PIDだけでは複数台を一意にできないため、最終確定はprotocol v1 handshakeと「成功1台」の条件で行う。

route変更をlive switchにすると、旧sessionのownership releaseと新sessionの初期stateを同時に扱う必要がある。t05ではこの複雑さを持ち込まず、設定保存とactive sessionを分離して次回起動境界で切り替える。

## Verification

- Host focused test: 100件成功、失敗0
- Input focused test: 141件成功、失敗0
- Desktop focused test: 77件成功、失敗0
- Architecture focused test: 6件成功、失敗0
- fake候補／exchangeで0台、1台、複数台、指定identity、version不一致、HELLO／READY、ALL_UP／close、設定保存、requested／active表示を確認
- 実COM列挙、実board handshake、実flashはt07まで未確認
