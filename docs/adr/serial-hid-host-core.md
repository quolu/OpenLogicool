# ADR: Serial HID host core

- Date: 2026-08-23
- Status: accepted
- Scope: `t03-serial-hid-core`

## Decision

Serial HIDのhost coreは次の三層だけで構成する。

1. `ISerialHidFrameExchange`は完成request frameを一度だけ書き、同じrequestの完成response frameを期限内に一度だけ返す。port列挙、再接続、再送は持たない。
2. `SerialHidProtocolSession`はHELLO／READY handshakeとSET_STATE／ALL_UP／HEARTBEATを一つずつ直列化する。timeout、transport fault、破損frame、FAULT、応答kind／sequence不一致はterminal faultであり、そのsessionを再利用しない。
3. `SerialHidEmitter`はMapping Runtimeのedge列を同方向checkpointへ分け、output参照数から完全HID snapshotを作る。SET_STATEのmatching ACK後だけownership stateをcommitする。

payload構築中のwrong up、変換不能usage、6KRO超過はwireへ送る前に拒否する。finite sequenceはcheckpointごとにSET_STATE→ACK→commitを完了してから次へ進む。timeout後に同じsequenceや次のsequenceを送らず、SendInputへfallbackしない。

## Reason

timeoutや破損応答の時点ではfirmwareがHID stateを適用済みか判定できない。同じrequestの再送や旧committed stateからの継続はownershipを二重化するため、安全な継続条件がない。sessionをterminal faultにすることで、再接続とall-upをresident lifecycleの明示操作へ残す。

`IOutputEmitter.Emit`は既存fast pathの同期境界である。Serial HID routeではACKが物理出力確定の条件なので、選択中emitterの同期ACK待ちはこの境界に置く。heartbeat、handled stop、background fault伝播、serial closeはt04、port discoveryと実serial transportはt05で所有する。

## Verification

- Input focused test: 141件成功、失敗0
- Architecture focused test: 5件成功、失敗0
- fake exchangeでHELLO／READY、chord、finite sequence、duplicate ownership、6KRO、timeout、transport fault、破損、FAULT、sequence mismatch、ALL_UP、HEARTBEATを確認
- 実serial write、実ACK、USB HID report、foreground受理、game内成功は未確認。後続taskで別々に判定する
