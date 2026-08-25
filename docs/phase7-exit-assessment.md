# Phase 7 Exit Assessment（Daily Mission Pilot）

- 作成: 2026-08-21（統括 bell-grok46・t07）
- 上位正本: [development-plan.md](development-plan.md) §Phase 7、§12.1
- campaign 受入: [phase7-campaign-plan.md](phase7-campaign-plan.md)
- 判定材料: Lattice plan `phase7-daily-pilot`（t01〜t06 done・t07 は本書）・[evidence/phase7-daily-pilot/](../evidence/phase7-daily-pilot/)
- 根拠4値: 確認済み／強い推定／未確認／非対応
- Exit 宣言: **2026-08-21 親（bell-grok46）**。技術成立の宣言でオーナーに止めない。H は未確認のまま残す

> Historical acceptance: 条件5のreview statusによる操作拒否は2026-08-25裁定で失効した。現在は利用者が`AllowedModes`へ明示したmodeだけを操作gateとし、review statusは表示情報に限定する。

## Exit 条件の判定

### 条件1: 初日の成功を verified としない — **確認済み**

`DailyTwoCycleReport.DayOneVerified` は常に false。day1／day2 が同じ known path でも昇格しない。focused `DailyTwoCycleTests` 2 green。実装 `f402dc0` は origin/main 祖先。

### 条件2: 翌日相当の別 session で known path を再現する — **確認済み**

`DailyTwoCycle.Record` は day2 が翌 virtual day の別 session で、day1 と同一 action path のときだけ受理する。同一 session や path 不一致は拒否。GameLab の daily reset 本体は Phase 1 の既存口を再実装していない。

### 条件3: 途中停止、manual intervention、Alt+Tab、capture loss、OutcomeUnknown から復帰できる — **確認済み**

`DailyRecovery.Plan` は Interrupted／ManualIntervention／ForegroundLost／CaptureLost／OutcomeUnknown のすべてから day2 の known path を再開候補として返す。既存 resume／fault 口は再実装しない。focused 5 green。実装 `cb251ae` は祖先。

### 条件4: 未知 branch を既存 verified path を壊さず追加できる — **確認済み**

`UnknownBranchAppend.Append` は `PlaybookCorrection.Revise` で新 Version だけへ node／edge を足す。旧 verified Version の serialize が不変。実装 `56d17ef` は祖先。

### 条件5: 規約上許可されない mode は技術的に可能でも無効 — **確認済み**

`GamePolicyGate.Evaluate` は Unverified／Changed／InterpretationUnknown で Assist／Auto を拒否する。Confirmed でも `AllowedModes` 外は拒否。SendInput 受理は入力に無い。実 ToS 解釈はしていない。実装 `b40346a` は祖先。

### 条件6: 各 ToDo focused green＋証跡＋着地。H は未確認のまま残してよい — **確認済み**

| task | feat | 判定 |
|---|---|---|
| t01 | `f402dc0` | 確認済み |
| t02 | `56d17ef` | 確認済み |
| t03 | `b40346a` | 確認済み |
| t04 | `0d80c80` | 確認済み（dispatch／SendInput なし） |
| t05 | `cb251ae` | 確認済み |
| t06 | なし | **未確認**（実 game 窓なし） |

席は t06／t07 を取っていない。

## H

実 game Observe Only は窓が無い。未確認のまま残す。一般対応と書かない。

## full regression

2026-08-21、t05 着地後の作業木で `dotnet test OpenLogicool.sln` を **1回**。失敗 0。計 **643** 件（Playbooks 132 を含む）。

## Grok read-only 監査

親が t01〜t05 の実装を読んだ。AI 経路から SendInput する口は無い。初日成功を Verified にする口は無い。重大な成立取り違えは無い。

## 対象外

- provider 選定
- Phase 8 の配布・署名
- G600 残置の実機確認
