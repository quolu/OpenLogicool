# firmware 1.1.4の実機導入

2026-10-10、Windows native。未導入だったfirmware修正の導入を完了した。

## 実機の書込み

利用者の写真でATmega32U4・16MHzのPro Micro互換基板、ピンヘッダ未実装のGND/RSTの穴を確認した。接点操作後、同じLocationPathsのArduino Leonardo互換AVR109機器（VID2341 / PID0036、COM5）が11:09:42に再到着し、11:09:46に除去された。通常動作時のCOM3 HID機器と操作した実物の対応が確認できた。CH340 COM4は更新対象にしていない。

正規flash scriptでverify付きuploadとCDC・keyboard・mouseの再列挙を確認した。`flash-verified.json`のbootloaderEntryはmanual-double-reset、uploadVerified=true。HEXのSHA-256は準備済み成果物と一致した。続く正規診断はREADY 1.1.4・全解放25ms成功。

## 自動更新と停止条件の再現試験

1.1.4導入後、同じ正規flash入口を`-WaitForReset`なしで再実行した。基板への手操作なしで標準1200-baud resetとverify付きuploadに成功した。`standard-auto-verified.json`と`standard-auto-upload.log`を参照。signatureはATmega32U4（1E 95 87）、verifyは6548bytes。次回の通常更新をPCだけで行えることを実測した。

- 無入力200ms、5回: 全成功。
- 無入力1000ms、2回: 全成功。
- 対応bootloaderの選択・別の接続口・非接続履歴・未知機器・複数候補などのfocused test、8件: 全成功。
- 待機表示変更のPowerShell構文確認: 成功。

無入力200msでSequenceViolationになった旧版の再現条件は解消した。保持中150ms全解放は既存native試験の確認範囲を維持している。実機hard kill再測定はG13の物理押下を要するため今回実施していない。firmware sourceは前回のnative試験から変更しておらず、同じgreen testと全体回帰を再実行していない。

## Bot再開

正規CLIでBotを新しい実行として再開した。NanoSerialHidのSpace down/upと回復観測の継続を確認した。手入力中はUserPausedとなり、全解放後3秒で再開する既存動作も記録した。ゲームはLv52で画面が進行しているが、この間に利用者の手入力も混在しており、画面の全変化をBotだけの成果とは扱わない。

録画原本・マクロ・割当は変更していない。過去のHELLO無応答を含む全ての通信障害の根本原因が解明済みとは扱わない。今回の達成範囲は、未導入firmwareの実機導入、標準自動更新、無入力の再現条件解消、Bot再開とNano送出の確認。
