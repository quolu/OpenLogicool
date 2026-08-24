# Phase 10 NIKKE Daily Drive Exit判定

- 判定日: 2026-08-24
- 結論: **Exit成立**
- 実証範囲: 教師付き探索でNIKKEの日課一覧を発見し、非ダイヤ日課一件をNano Serial HIDだけで達成・受領した。

## 成立結果

| 条件 | 結果 | 根拠 |
|---|---|---|
| STEP 0 | 確認済み | [GameWith SummaryOnly](../rag/openlogicool/nikke-daily-gamewith-summary-2026-08-24.md)。raw HTML／全文Markdown／画像は保存していない |
| 日課一覧の発見 | 確認済み | 右上青`!`中心→`MISSION > デイリー`。初回`0/100`、`probe-output/live-discovery-nano-coordinate-20260824-183202-938.json`、`probe-output/live-discovery-observe-20260824-183245-867.json` |
| 非ダイヤ日課一件 | 確認済み | `基地防御報酬を1回獲得する 0/1→1/1`。通常の`報酬獲得`だけを操作し、`まとめて殲滅`は未操作 |
| 達成報酬 | 確認済み | 日課受領後`10ポイントを獲得しました`、`10/100`。`probe-output/live-discovery-nano-coordinate-20260824-184030-553.json`、`probe-output/live-discovery-observe-20260824-184120-103.json` |
| ダイヤ消費0 | 確認済み | 開始`4,716`、終了`4,746`。指揮官レベル489到達時の画面報酬`+30`であり、支出は0 |
| 入力境界 | 確認済み | NIKKEへの全click／keyはNano Serial HID。各coordinate／escape証拠はSendInput 0、Computer Use input dispatch 0、fallbackなし |
| 外部AI／API | 確認済み | `live-discovery-observe`終端証拠は外部AI送信0、API key 0、費用0 |

## 確定した画面遷移

| 起点 | 操作 | 結果 |
|---|---|---|
| ロビー右上の赤い`N` | click | `SUB MENU`。日課入口ではない |
| `MISSION PASS`左のリスト | click | Pass切替リスト。日課入口ではない |
| ロビー右上の青い`!`中心 | click | `MISSION > デイリー` |
| 日課`基地防御報酬`の`すぐに移動` | click | 前哨基地 |
| 前哨基地右下`100%` | click | `前哨基地の防衛` |
| `報酬獲得` | click | 基地防御報酬、指揮官レベル489、30ダイヤ受領 |
| 日課`基地防御報酬 1/1`の`受取` | click | 10ポイント受領、`10/100` |

探索中に`!`のY座標をタイトルバー込みframeへ誤変換し、背景台詞を出した。また作戦右端の小さい`!`はフィールドへ遷移した。いずれも購入・資源消費はなく、Nano Escapeで戻った。入口の確定値は正規化座標`(0.902, 0.122)`である。

## 製品実装と検証

- `live-discovery-nano-coordinate`を追加した。fresh WGC frameの正規化座標、Nano pointer移動、Win32 cursor readback、対象patch、after frameを一つの証拠へ束縛する。
- animation／hover中のcontrol向けに、exact SHA-256と64-bit difference hashを選択できる。dHash距離は最大16/64、対象window・座標・cursor readback条件は維持し、基準不一致時はclickせず停止する。
- focused test: `LiveDiscoveryNanoCoordinateSmokeTests` 29件green。
- 関連test: `OpenLogicool.Probe.Tests` 55件green。
- `git diff --check`と対象path限定commit／pushを終端監査とする。

## Claim境界

許されるclaimは「NIKKE PC版で、教師付き探索により基地防御報酬の日課一件をNano Serial HIDだけで完了し、ダイヤ消費0を画面で確認した」まで。全日課完遂、教師なし探索、長期自律運転、一般game対応、`Verified Autonomous Playbook`は未確認でありclaimしない。
