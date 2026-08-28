# Phase 13 Macro Product Flow terminal acceptance（2026-08-29）

## 結論

Phase 13 Exitは成立した。ChatGPT subscription Codexによる目的macro作成、AI監視付き修復、AI 0再現、実データroute合成、G13／G600割当、再起動復元、Nano-only実行が一つの製品journeyとして成立した。

## 実データ合成

- source 1: NIKKEロビー→MISSIONデイリー一覧、Compiled、1 step。
- source 2: MISSIONデイリー一覧→NIKKEロビー、Compiled、1 step。
- composed route: `macro:composed:00b2081f045145b58b4d92819a26b762`。
- composed revision: 1。
- source route変更: 0。
- 別Host process AI 0: 2/2 step Moved、AI call 0、revision 1不変、Completed。
- product evidence: `probe-output/phase13-composed-ai0-restart.json`。

## G13／G600割当

- composed macro token: `Macro:free:bWFjcm86Y29tcG9zZWQ6MDBiMjA4MWYwNDUxNDViNThiNGQ5MjgxOWEyNmI3NjI:latest`。
- 実DBコピー上のworkspace: `phase13-composed-acceptance`。
- G13: G1／base。
- G600: G9／base。
- dry-run compile: 成立。
- workspace revision 1保存: 成立。
- G13／G600 MappingProfile生成: 成立。
- 別process再open export: token、G1、G9一致。
- 本番の既存G13／G600設定変更: 0。
- FastPath button down一回→macro queue: focused testで成立。

## 追加欠陥の根治

1. action実行後にroute commitが0件でもCodex `finish`だけでCompletedになる欠陥を拒否した。action 0のzero-step goalだけは許す。
2. dynamic tool errorをresult evidenceへstack付きで保存する。
3. 文字anchorのないpageはoptional state visual patchで保存・再認識する。
4. 過去run由来の同一LearnedState IDと同一Structure scene signatureの重複を決定的canonicalへ収束する。
5. structure source nodeは比較用全page sceneを正とし、実行targetは`probe-target`としてnode signatureから除外する。
6. firmware 1.1.2はfail-closed releaseが1秒継続した場合、AVR watchdogでUSBを自己再列挙する。action自動再送はしない。

## Nano実測

- firmware 1.1.2 native recovery timer: 1000ms、wraparoundを含めgreen。
- Arduino build: 6388 bytes、RAM 262 bytes。
- wedgeしたfirmware 1.1.1から1200-baud bootloader経由のverify付きflash: 成立。
- firmware hex SHA-256: `2dce4790979060787efc0056d2720a8b8ec37c67e572b304c6ef68e337a1249c`。
- flash後CDC／keyboard／mouse再列挙、firmware 1.1.2 handshake: 成立。

## UI受入の扱い

オーナー裁定により、リモート中はUI入力をスキップし、手動入力の1:1確認は後日行う。Phase 13ではUIと同じpublic Host intent、実SQLite、macro token、workspace compile/save/reopen、G13／G600 bindingを受入根拠とする。手動UI目視を実施済みとは扱わない。

## Exit後確認

- Game Operator／Input Studioの手動入力1:1 journey。
- NIKKE shopの日次／週次reset後AI 0再現。

## 検証

- focused／関連test: Perception 32、Exploration 53、Input 157、Host 252、Desktop 98、Playbooks 164、Persistence 50、architecture 8の合計814件green。firmware native testとArduino buildもgreen。
- 最終full regression: 22 test project・1258件green・failed 0・skipped 0。実装完了後に一回だけ実行した。
- 開発版再install: 成立。install先の`OpenLogicool.Host.exe macro list`はexit 0で、shop route、日課route、composed 2 step routeを再読した。
