# Phase 8B campaign — Game Operator Distribution

- status: **active**
- 起票: 2026-08-21（工程表どおり。Phase 8A Exit 後。判断をオーナーへ戻さない）
- 統括: ベル（Grok 4.6）。実装 Terra×high（Codex）／監査 Grok 4.6×medium
- 実行 TODO の正本: **Lattice plan `phase8b-game-operator-dist`**
- 上位正本: [development-plan.md](development-plan.md) §Phase 8B、§14.1 Shared Distribution Gate、§14.3 Game Operator Public Gate
- 先行: Phase 4〜7・8A Exit 成立。provider は未選定。実 game Observe Only は未確認。Authenticode／clean VM は 8A の未確認のまま

## 目的

Durable Automation と AI 機能を、Input Studio 本体の公開可否から独立して配布可能にする。Input Studio の既存機能と設定を AI／network 障害で損なわない。Game Operator Public Gate は確認済みの行だけで判定し、未確認は未確認のまま残す。

## 統括レーン判定と F/A/H

①実 game Verified の独立 live は人待ちとして組込む。②多段受入。④証跡。Exit オーナー待ちは組まない。

- **F**: gate 判定、commit・push、t10 Exit
- **A**: support matrix、schema rollback、active Run の update 抑止、capability 別 release、再起動後 ownership reconcile、Input Studio 隔離、data／cost 口、eval 記録
- **H**: 実 game 用 Verified Step の独立 live session 証拠。席は取らない。窓が無ければ未確認のまま残す。証拠を捏造しない

## 円卓

入口は peertable room `OpenLogicool`。setup.sh／parent-join はしない。pull run は本 plan 用に新規。席数は実装 2＋監査 1 のまま増やさない。

| 役割 | 配置 |
|---|---|
| 統括 | Grok 4.6（bell） |
| 実装 | Terra×high Codex |
| 監査 | Grok 4.6×medium |

待機中は `[次の行動]` 自己DMを出さない。preflight 失敗は席を立てず、別 model へ落とさない。工程が変わったら席が自分で `set-mission.sh` する。親は代行しない。

## 非目標

- provider を仮決めする
- GameLab Verified を実 game へ継承する
- 実 game を席が勝手に動かす
- 8A の Authenticode／clean VM／packaging 方式をここで採択する
- LGS Parity を名乗る
- 装飾 UI
- 既存 Playbook／journal／fault／ObserveOnly／TeachSupervised／VerifiedEnvScope／EvalHarness／watchdog／fast path を再実装する

## 受入条件（§Phase 8B Exit）

1. Shared Distribution Gate と Game Operator Public Gate を、確認済みの行だけで判定する。未確認は未確認のまま残す
2. 実 game 用 Verified Step は独立 live session 証拠を持つか、未確認のまま残す
3. 再起動後は output ownership を reconcile するまで次 dispatch を禁止する
4. Input Studio の既存機能と設定は AI／network 障害でも使える
5. 各 ToDo focused green＋証跡＋着地。H は未確認のまま残してよい。通し試験は Exit だけ

## 運用

H の t09 は席が取らない。t10 は親。remaining A は t01〜t08。t09 は A の口の後で親が未確認のまま閉じる。MAX_TODOS=8 のため、t09 が ready に並ぶ最初の compile は conversation で席を動かす。A が減ってから remaining を witness compile する。

## Lattice task 仕様（正本は store）

### t01-go-support-matrix

Game Operator の support matrix と公開情報を製品面へ置く。Data Flow、provider、Game Policy を出す。確認済みだけ Supported。未確認を Supported にしない。provider は未選定と書く。既存 `GamePolicyGate` を再実装しない。focused。

### t02-schema-rollback

Playbook／journal／Knowledge Pack の schema update と rollback contract。未知 version は読み飛ばさず fail。rollback は旧 schema へ戻せる口だけを置く。既存 store／validator／journal fold を再実装しない。focused。

### t03-active-run-update-hold

active Run 中の update を抑止する。抑止中の update は開始しない。Run 終了後の resume compatibility を契約として固定する。既存 `InstallLifecycle` と Run pin を再実装しない。focused。

### t04-capability-release-gates

Observe Only、Teach、Supervised、Verified の capability 別 release 設定。各 mode は自分の gate を迂回できない。既存 `ObserveOnly`／`TeachSupervised`／`GamePolicyGate`／`VerifiedEnvScope` を再実装しない。focused。

### t05-restart-ownership-reconcile

host 再起動後、output ownership の reconcile が終わるまで次 dispatch を禁止する。既存 watchdog の死亡時 release と `AttemptDispatchGate` を再実装しない。focused。

### t06-input-studio-isolation

AI／network／capture が落ちても Input Studio の既存機能と設定は動く。fast path に AI を待たせない。既存 architecture 禁止を再実装せず、障害時も Input Studio が使える契約を focused で固定する。

### t07-data-flow-controls

image 保存、cloud 送信、削除、provider、cost を確認・制御できる口。provider 未選定の間 cloud 送信は開始しない。screen／secret を既定 bundle に入れない。既存 diagnostic bundle を再実装しない。focused。WPF 磨きはしない。

### t08-eval-threshold-record

frame corpus と AI eval の事前固定 threshold、dataset／model／prompt／parameter の記録口。既存 `EvalHarness` を再実装しない。acceptance dataset で prompt を調整しない。provider を選定しない。focused。

### t09-live-verified-session

実 game 用 Verified Step の独立 live session 証拠。席は取らない。窓が無ければ未確認のまま残す。GameLab Verified を実 game へ写さない。証拠を捏造しない。

### t10-phase8b-exit

full regression 1回、Grok read-only 監査、`docs/phase8b-exit-assessment.md`。親が宣言。席は取らない。H 未確認は未確認のまま書く。Public Gate を未確認行で成立扱いにしない。
