# Phase 10 NIKKE Daily Drive campaign

- 状態: 完了（2026-08-24 Exit成立）
- 開始日: 2026-08-24
- 正本: 本書と[製品・開発計画](development-plan.md) Phase 10
- 前提: [Phase 9 Exit判定](phase9-exit-assessment.md)

> Historical scope: 本campaignのダイヤ、購入、戦闘等の制限は、この一件実証へ明示したGame Policyだけに属する。コア製品の既定禁止語または通常操作gateとして継承しない。

## 目的

GameWithの公開情報を未信頼の参考仮説として取り込み、NIKKEの実画面から日課一覧を発見する。その一覧を根拠に、ダイヤを消費しない日課を一件、Nano Serial HIDだけで実行し、画面の進捗または報酬変化で完了を確認する。

## オーナー裁定

- 日課達成に必要なゲーム内操作は許可する。
- **ダイヤ消費は禁止する。** 開始時に観測したダイヤ残高`4,716`を基準とし、終了時に下回らないことを確認する。報酬による増加は許容し、増加理由を記録する。
- 実通貨による購入とアカウント設定変更・削除は行わない。
- NIKKEへのkeyboard／mouse入力はNano Serial HIDだけを使う。SendInput、Computer Use、直接Win32 inputをfallbackにしない。
- Computer Useは画面観測だけに使い、NIKKEの前面化、click、key、scrollには使わない。
- 外部AI APIと従量課金APIは使わない。

## 成功条件

1. GameWithの日課記事をSTEP 0の`SummaryOnly`契約で取得し、出典、取得時刻、日課候補、更新時刻、画面名称を短い構造化要約として残す。raw HTML、画像、全文Markdownは永続化しない。
2. NIKKEロビーから日課一覧への入口を、fresh frameへ束縛したNano操作と操作後の再観測で特定する。
3. 一覧画面に表示された日課項目、達成状態、受取可能状態を観測し、GameWith仮説とgame内事実を区別して保存する。
4. ダイヤを必要としない日課を一件選び、Nanoだけで完了する。
5. 成功は入力ACKでなく、日課進捗、受取状態、報酬表示のいずれかの画面変化で確定する。
6. 操作後のダイヤ残高は開始時`4,716`を下回らず、差分と理由からダイヤ消費0を証拠化する。
7. SendInput 0、Computer Use dispatch 0、外部AI送信0、外部AI API費用0を記録する。

## 停止条件

- ダイヤ消費を示す確認、価格、残高減少が表示された時は、確認を押さず停止する。
- 対象frame、window、pointer位置、操作結果のいずれかが一意でない時は、再送せず再観測する。
- NanoのACKが不明、COM portが不明、target windowが不明な時はfallbackせず停止する。
- 日課と無関係な購入、アカウント、削除、外部送信へ遷移した時は停止する。
- 戦闘や資源利用は日課に必要な場合だけ許可するが、ダイヤを求める分岐へ進まない。

## 工程

| Task | 内容 | 受入 |
|---|---|---|
| t01 | STEP 0 GameWith調査 | **完了**。SummaryOnly要約、出典、候補日課、game内未確認表示 |
| t02 | frame-bound Nano探索操作 | **完了**。fresh frame、normalized target、Nano move/click、no fallback、before/after evidence |
| t03 | 日課一覧の発見 | **完了**。右上青`!`中心→`MISSION > デイリー`を再観測 |
| t04 | 非ダイヤ日課一件の完了 | **完了**。基地防御報酬`0/1→1/1`、受領後`10/100`、ダイヤ`4,716→4,746`（報酬+30、消費0） |
| t05 | 終端監査 | **完了**。success条件全件、関連test、実測証拠、claim境界 |

## F／A／Hと進行

- F: ダイヤ禁止、入力経路、操作許可、実ゲームdispatch、成功判定、Exit判定。親が直接裁定する。
- A: 必要になった最小probe実装とfocused test。今回は同一repo・同一実ゲーム状態へ直列に接続するため委譲しない。
- H: オーナーが機械を動かさないと取得できる観測が発生した場合だけ。現時点では予定しない。
- 並列化しない理由: GameWith仮説→画面特定→日課選定→実行結果が同じ実ゲーム状態を順に更新し、独立ToDoではない。

## 非目標

- 全日課の完遂
- ダイヤ消費を伴うミッション達成
- GameWith本文の複製保存
- NIKKE全体のVerified化
- 教師なし自律運転または`Verified Autonomous Playbook`のclaim
- 課金、プレミアムパス購入、アカウント操作

## 終端判定

全成功条件は成立した。GameWith仮説は[SummaryOnly要約](../rag/openlogicool/nikke-daily-gamewith-summary-2026-08-24.md)、実game証拠と判定は[Phase 10 Exit判定](phase10-nikke-daily-drive-exit-assessment.md)を正とする。公開claimは教師付きの一件実証までであり、全日課完遂や教師なし自律運転には拡張しない。
