# Phase 7 campaign — Daily Mission Pilot

- status: **completed**（2026-08-21 Exit 宣言・[phase7-exit-assessment.md](phase7-exit-assessment.md)）
- 起票: 2026-08-21（オーナー指示「次に進もう」Phase 6 Exit 後）
- 統括: ベル（Grok 4.6）。実装 Terra×high（Codex）／監査 Grok 4.6×medium
- 実行 TODO の正本: **Lattice plan `phase7-daily-pilot`**
- 上位正本: [development-plan.md](development-plan.md) §Phase 7、§12.1 Game Policy Record
- 先行: Phase 6 Exit 成立。provider は未選定のまま。GameLab の daily reset 単体は Phase 1 で成立済み

## 目的

同日やり直せない daily 進行で、逐次学習と翌日相当の再現を実証する。初日の成功を Verified にしない。未知 branch は既存 verified path を壊さず追記する。規約上許可されない mode は技術的に可能でも無効。

## 統括レーン判定と F/A/H

①実 game・consent は人待ちとして組込む。②多段受入。④証跡。Exit オーナー待ちは組まない。

- **F**: policy 契約、commit・push、t07 Exit
- **A**: 2 cycle、未知追記、policy gate、shadow、daily 復帰
- **H**: 実 game Observe Only。席は取らない。窓が無ければ未確認のまま残す

## 円卓

入口は peertable room `OpenLogicool`。setup.sh／parent-join はしない。pull run は本 plan 用に新規。席数は実装 2＋監査 1 のまま増やさない。

| 役割 | 配置 |
|---|---|
| 統括 | Grok 4.6（bell） |
| 実装 | Terra×high Codex |
| 監査 | Grok 4.6×medium |

待機中は `[次の行動]` 自己DMを出さない。preflight 失敗は席を立てず、別 model へ落とさない。

## 非目標

- provider を仮決めする
- 初日成功を Verified にする
- GameLab Verified を実 game へ継承する
- 実 game を席が勝手に動かす
- 装飾 UI
- Phase 8 の配布・署名

## 受入条件（§Phase 7 Exit）

1. 初日の成功を verified としない
2. 翌日相当の別 session で known path を再現する
3. 途中停止、manual intervention、Alt+Tab、capture loss、OutcomeUnknown から復帰できる
4. 未知 branch を既存 verified path を壊さず追加できる
5. 規約上許可されない mode は技術的に可能でも無効
6. 各 ToDo focused green＋証跡＋着地。H は未確認のまま残してよい。通し試験は Exit だけ

## 運用

H の t06 は席が取らない。親がオーナー窓の用意後だけ start する。t07 は親。

## Lattice task 仕様（正本は store）

### t01-two-cycle-not-verified

GameLab で virtual day を2回回す。day1 の成功は Verified にしない。day2 相当の別 session で known path を replay する。既存 daily reset を再実装しない。focused。

### t02-unknown-branch-append

未知 branch を追記する。旧 verified Version は書き換えない。`PlaybookCorrection` の新 Version だけが未知を持つ。focused。

### t03-game-policy-gate

Game Policy Record。未確認・変更検出・解釈不明は automation disabled。Observe／Assist／Auto を mode 別に許可。SendInput 受理を規約許可の証拠にしない。import Playbook は gate を迂回できない。実 ToS 解釈はしない。focused。

### t04-shadow-compare

利用者の実操作と AI proposal を比較する shadow 口。dispatch しない。SendInput しない。fake planner で閉じる。本番 provider を埋め込まない。

### t05-daily-recovery

daily cycle の途中停止、manual intervention、foreground 喪失、capture loss、OutcomeUnknown から復帰する。既存 fault／resume 口を再実装しない。focused。

### t06-real-observe

実 game の Observe Only。席は取らない。窓が無ければ未確認のまま t07 へ残す。一般対応と書かない。

### t07-phase7-exit

full regression 1回、Grok read-only 監査、`docs/phase7-exit-assessment.md`。親が宣言。席は取らない。H 未確認は未確認のまま書く。
