# ADR: Resident output session lifecycle

- Date: 2026-08-23
- Status: accepted
- Scope: `t04-resident-output-session`

## Decision

resident fast pathの出力lifecycleを`IResidentOutputSession`へ分離する。

- SendInput routeは`SendInputResidentOutputSession`が`GuardedOutputEmitter`とWindows watchdogを所有する。
- Serial HID routeは`SerialHidResidentOutputSession`が`SerialHidEmitter`、50ms heartbeat、ALL_UP ACK、serial transport closeを所有し、Windows watchdogを起動しない。
- `ResidentInputHost.Stop`は新規inputを止めてMapping Runtime所有outputをreleaseした後、output sessionを停止する。Serial HIDの正常停止はALL_UP ACK後だけtransportを閉じる。
- heartbeat／transport faultはoutput sessionの`BackgroundFailure`からresidentの`Failure`へ伝播し、CLIと`ui --resident`を停止させる。fault済みprotocol sessionは再利用せず、抜線後も暗黙resumeしない。
- Serial HID routeとG600 onboard modeは排他にする。永続onboard stateがある起動と、Serial HID常駐中の新規onboard書込みを明示拒否する。

## Reason

SendInputのrelease ownerはWindows watchdog、Serial HIDのrelease ownerはfirmware leaseであり、同じlifecycleへ混ぜると二重監視またはrelease不能を隠す。route固有資源を一つのsessionへ閉じ込め、Hostにはemitter、background failure、Start／Stopだけを見せる。

Serial HIDのheartbeat fault後はrequestの適用有無が確定しないため、ALL_UPを含む追加requestを送らずtransportを閉じる。firmware leaseが150msでall-upを所有する。正常なhandled stopだけはedge release ACKの後にALL_UP ACKを取り、closeする。

## Verification

- Host focused test: 90件成功、失敗0
- Input focused test: 141件成功、失敗0
- Architecture focused test: 5件成功、失敗0
- fake exchangeで正常停止順、heartbeat timeout伝播、fault後のno retry／no implicit resume、transport close、onboard排他を確認
- hard crash時の物理release時間、実抜線、実serial closeは後続実機taskまで未確認
