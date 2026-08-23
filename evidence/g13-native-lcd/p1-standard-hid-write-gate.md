# Phase 1: Windows標準HID write gate

- 実施日時: 2026-08-23
- 判定: 成立（確認済み）
- transport: Windows標準HidUsb + `WriteFile`
- probe: `g13-lcd-smoke`、`g13-adapter-smoke --until-g1`
- raw evidence: `probe-output/g13-lcd-smoke-20260823-194726-440.json`、`probe-output/g13-adapter-smoke-20260823-201233.json`（workspaceローカル、非commit）

## 実測

- G13は単一top-level HID collectionとして列挙された。
- usageは`0xFF00:0x0000`、input reportは8 bytes、output reportは992 bytes、feature reportは最大258 bytesだった。
- report ID `0x03`、31-byte zero header、960-byte framebufferのsolid frameを`WriteFile`へ渡した。
- `WriteFile`は成功し、992/992 bytesの全長送信を返した。
- 送信後、オーナーがLCDの大きな「G13」表示から白一色への変化を目視した。
- その後、同じHID collectionのRaw InputでG1 down（sequence 1）とup（sequence 2）を順番どおり取得した。
- input dropは0だった。
- driver、firmware、device profile、registry、LGS設定は変更していない。

## 反対経路の判別

- 最初のsparse patternは`WriteFile`が992/992 bytesを返したが、即時の表示変化を目視できなかった。
- 同じreportを診断限定の`HidD_SetOutputReport`へ渡す実験はWin32 error 31で失敗した。
- errorを別経路で握り潰さず、`HidD_SetOutputReport`は不採用とした。
- high-contrastなsolid frameでLCD反映まで確認できたため、標準`WriteFile`経路を採用する。
- sparse patternが見えなかった理由は未測定であり、原因を断定しない。

## Exit判定

1. 992-byte frame全長write: 確認済み。
2. LCD上の識別可能な変化: 確認済み（白一色）。
3. write後のG13 input down/up、drop 0: 確認済み。
4. driver／firmware／profile／LGS設定の非変更: 確認済み。

Phase 1 Exitは成立した。Phase 2はこの`WriteFile`経路だけをresident LCD runtimeへ組み込む。
