# AI監視付き修復とAI 0再現（2026-08-28）

## 目的

NIKKEロビーを開始pageとして、アプリがChatGPT subscription Codexを呼ぶAI監視付きモードで日課取得routeを修復し、その保存routeを別runのAI 0で最初から最後まで再現する。

利用者goal:

> NIKKEの日課の未完了項目と進捗を最下端まで取得して一覧化する。報酬受取、資源消費、課金、募集、戦闘は行わない。

## 監視付きモード

- 監視役はOpenLogicoolがCodex App Serverで起動したCodex。親AIからの座標・Back・scroll・修復指示は0。
- 入力はNano Serial HIDのみ。Computer Use／SendInputは0。
- 14件の日課、0/100、最下端を取得。禁止操作0。
- 最終保存routeはrevision 69、9 step。
- Windows情報scroll adapterは、対象領域の新規OCR textまたは同一textの縦移動をCompare直後に判定し、全画面の動的banner変化を一覧進行へ数えない。

## Nano通信障害と根治

長時間run中、WindowsにはCOM8が残る一方でfirmwareがHelloへ応答しないtimeoutを実測した。Arduino AVR 1.8.8の実コードを確認し、CDC `Serial.write`とHID `SendReport`がblockingであること、firmwareがfail-closed release retryをserial受信より先に実行すること、HostのHello期限が80msであることを原因として確定した。

修正:

- firmware 1.1.1: serial受信をrelease retryより先に処理。
- releasePending中もHelloを明示回復要求として処理。
- Host: Hello timeoutを1秒、通常action timeoutを80msのまま分離。
- actionの自動retry、SendInput fallback、lease 150msの緩和は0。
- Windows build／flash scriptをPowerShell 7と正しいUSB macro引数へ適合。

実測:

- firmware native test: golden vector 8件、fault、lease 150ms、mouse delta green。
- Arduino CLI固定toolchain build: flash 6196 bytes、RAM 257 bytes。
- verify付きflash: firmware hex SHA-256 `66c3e9f0427d3086cbd2be0aac25560c29ae7a6de4d4b0a06bc3da883614d121`。
- flash後CDC＋keyboard＋mouse再列挙: 成立。
- game非依存 `serial-hid-test --repeat 100`: 100/100、firmware 1.1.1、659ms、AllUpのみ。
- 17分および16分のAI監視付きlong run: Nano timeout再発0。

## AI 0再現

- 開始page: NIKKEロビー。
- PlaybackMode: `AiFree`。
- 保存route: revision 69、9 step。
- step 0〜9: すべて保存action、Moved。
- terminal: `Completed`。
- AI call: 0。
- route revision: 69のまま。
- Computer Use／SendInput／fallback／自動retry: 0。
- 実走証拠: `probe-output/ai-zero-route-replay-text-motion-20260828.json`。

## 検証

- focused: Codex／route／scroll判定25件green、Serial HID Input 20件green、Host Serial HID 23件green。
- 関連test: Input 157・Host 250・Probe 61・architecture 8、合計476件green。
- firmware native test、Arduino build、verify付きflash green。
- 最終full regression: 22 test project・1252件green、失敗0、skip 0。

## 判定

- アプリ内CodexによるAI監視付き完走: **確認済み**。
- 同じ保存routeのAI 0完走: **確認済み**。
- 正常step保持、route revision不変: **確認済み**。
- Nano CDC応答停止の根治と長時間再発なし: **確認済み**。
- NIKKE以外のgame、戦闘・課金・消費操作: **未確認／対象外**。
