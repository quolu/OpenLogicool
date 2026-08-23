# t10 Fable独立read-only反証監査

- 日付: 2026-08-23
- reviewer: Claude Fable 5、別session、read-only
- session: `9f2952bc-2900-491a-8c60-9e4c1566f3ab`
- operation: `sha256:c2623708b2ef4b30bcf7f0eb651f12a98833514f1ae55b7bf45325ca9947225a`
- 対象: protocol session、ACK後commit、fault境界、no retry／fallback、release責務、firmware lease、pump fault、host lifecycle、Exit 11条件、旧失敗probe、t09観測境界、公開claim

## 判定

初回判定は`HOLD`。技術本体は`CLOSE`相当でP0なし、終了前修理1件と仕分け1件を検出した。下記対処後は`CLOSE`を塞ぐ指摘なしと判定した。

## 反証結果

- ACK後commit: tentative stateはmatching ACK後だけcommitされ、sequenceとbase revisionの一致も検査する。
- no retry／fallback: timeout、transport、protocol破損、sequence不一致、FAULTはterminal faultとなり、SendInputへ切り替わらない。
- release責務: handled stopはpump release後に`ALL_UP` ACKを取る。background fault／hard killはheartbeat停止後のfirmware lease失効でfail closedする。
- lease失効後の遅延frame: firmwareは`protocolReady=false`となり、後着`SET_STATE`を拒否するため幽霊再押下しない。
- heartbeat飢餓: request timeout 80msと50ms heartbeatは150ms lease内で、timeout時もterminal faultからlease解放へ倒れる。
- 条件1〜10: 証拠と表示範囲は整合。条件11だけAGENTS現在地が未反映だった。
- 旧`passed=false` probeとt09のACK／game内反応／Windows hook未観測の分離は妥当。

## 指摘と対処

| 優先度 | 指摘 | 対処 |
|---|---|---|
| P1 | Exit条件11が参照するAGENTS現在地にSerial HID記述がない | AGENTS.mdへcampaign完了、成立範囲、公開claim、未確認範囲を追記 |
| P2 | 未追跡probe JSON 6件の仕分けが証拠文書にない | [t10終端受入](t10-campaign-exit.md)へ各ファイルを明示除外として記録 |
| P2 | FastPathPumpのrelease失敗診断がSerial HIDでも旧SendInput watchdogを名指しする | 出力経路中立の文言へ修正し、専用testを追加 |

## 独立性の扱い

Peertable room `OpenLogicool`の監査席へ依頼したが回答がなく、オーナーの「一回Fableにアドバイスをもらう」指示により別sessionのFable監査へ代替した。reviewerはcampaign実装・受入に関与せず、実装、firmware、証跡、diffをread-onlyで確認し、反対仮説を立てて実際に指摘を検出したため、計画の「独立read-only反証監査1回」として採用する。

同じmodel系列でありcross-provider監査ではない。この制約は残るが、計画はproviderを指定しておらず、オーナーの代替指示もあるためExitを止めない。
