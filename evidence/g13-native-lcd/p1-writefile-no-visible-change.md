# Phase 1: WriteFileは全長完了したがLCD目視変化なし

- 実施日時: 2026-08-23
- 判定: このsparse pattern試行は不成立（transportの最終判定ではない）
- probe: `g13-lcd-smoke`
- raw evidence: `probe-output/g13-lcd-smoke-20260823-192947-645.json`（workspaceローカル、非commit）

## 実測

- Windows HID descriptorはG13を単一top-level collectionとして列挙した。
- usage: `0xFF00:0x0000`
- input report: ID 1、8 bytes
- output report: ID 3、992 bytes
- feature report: 最大258 bytes
- `CreateFile`はread/writeで成功した。
- report byte 0を`0x03`、byte 1〜31を0、byte 32〜991を960-byte framebufferとして`WriteFile`した。
- `WriteFile`は成功し、`numberOfBytesWritten=992`を返した。
- オーナー目視は「みえない」。識別patternの表示変化はなかった。
- 成立時だけ押す契約だったG1は押さず、probeは60秒で未確認終了した。
- 実行時にLGS、G HUB、OpenLogicool常駐processは存在しなかった。

## 判定

`WriteFile`のAPI成功をLCD表示成功として扱わない。この試行では線の細い識別patternを目視できなかったため、
この時点ではG13 LCDの製品経路を未成立と判定した。

frame形状はdescriptor、Linuxのinterrupt OUT実装、macOSのHID output report実装で一致している。
次の判別実験は、同じ992-byte reportをWindowsの`HidD_SetOutputReport`で一度だけ送り、
control transferの`SET_REPORT`経路との差を確認する。

`HidD_SetOutputReport`はMicrosoftが無応答の可能性を警告するため診断限定とし、
実行前に影響とG13抜差しによる復旧をオーナーへ明示する。成功しても自動fallbackにはしない。

## 後続判定

後続のsolid frameは同じ`WriteFile`経路で992/992 bytes送信され、オーナー目視でLCDが白一色へ変化した。
さらにwrite後のG1 down/upとdrop 0も確認した。このためtransportの最終判定は成立へ更新した。
正本は`p1-standard-hid-write-gate.md`とする。
