# Phase 8B Exit Assessment（Game Operator Distribution）

- 作成: 2026-08-22（統括 bell-grok46・t10）
- 上位正本: [development-plan.md](development-plan.md) §Phase 8B、§14.1、§14.3
- campaign 受入: [phase8b-campaign-plan.md](phase8b-campaign-plan.md)
- 判定材料: Lattice plan `phase8b-game-operator-dist`（t01〜t09 done・t10 は本書）・[evidence/phase8b-game-operator-dist/](../evidence/phase8b-game-operator-dist/)
- 根拠4値: 確認済み／強い推定／未確認／非対応
- Exit 宣言: **2026-08-22 親（bell-grok46）**。技術成立の宣言でオーナーに止めない。H は未確認のまま残す
- 公開 claim: **Game Operator Preview**。実 game の Verified 自律実行は名乗らない

## Exit 条件の判定

### 条件1: Shared Distribution Gate と Game Operator Public Gate を確認済みの行だけで判定する — **確認済み（未確認行は未確認のまま）**

確認済みとして数える行だけを合格にした。未確認を Supported に昇格していない。両 Gate の全項目合格は宣言しない。

Game Operator Public Gate の確認済みの行:

- GameLab での Durable Automation（crash boundary、停止、修正、再開）
- AI proposal の schema／catalog／state／risk の dispatch 前拒否
- Data Flow contract の保存・送信境界
- game ごとの policy record による Assist／Auto gate
- schema update と未知 version の fail、rollback 口（t02）
- active Run 中の update 抑止と pin 完全一致 resume（t03）
- Observe Only／Teach／Supervised／Verified の capability 別 release（t04）
- 再起動後の output ownership reconcile 完了まで dispatch 禁止（t05）
- AI／network／capture fault 時の Input Studio 隔離（t06）
- image 保存／cloud／削除／provider／cost の制御口（t07。WPF 磨きは対象外）
- eval の事前固定 threshold と dataset／model／prompt／parameter 記録口（t08）

未確認のまま残す行:

- AI provider と provider data policy（未選定）
- 実 game の Observe Only と game policy の live 確認
- 実 game 用 Verified Autonomous Playbook（t09）
- 選定済み provider での eval 実測が threshold を満たすこと
- Authenticode 署名と timestamp（8A 持ち越し）
- MSIX／Sparse／MSI の公開採択と clean VM 実測（8A 持ち越し）
- public name／trademark／privacy notice の承認
- G600 leftover の実機確認

`GameOperatorSupportMatrix.PublicClaim` は `Game Operator Preview`。Supported は GameLab 限定の4行だけ。provider と実 game の3行は `Unverified`。

### 条件2: 実 game 用 Verified Step は独立 live session 証拠を持つか、未確認のまま残す — **未確認（そのまま残す）**

2026-08-22、`nikke_launcher` process は存在した。NIKKE 本体の独立 live session は観測していない。launcher を本体と扱わない。GameLab Verified を実 game へ写さない。証拠を捏造しない。[t09](../evidence/phase8b-game-operator-dist/t09-live-verified-session.md)。

### 条件3: 再起動後は output ownership を reconcile するまで次 dispatch を禁止する — **確認済み**

`RestartOwnership.AfterHostRestart` は `PendingReconciliation` で始まり `CanDispatch` は false。release 未確認の `CompleteReconciliation(false)` は解錠しない。確認後だけ `Reconciled`。既存 watchdog の死亡時 release と `AttemptDispatchGate` は再実装していない。focused 3 green。実装 `4fdda5d` は origin/main 祖先。

### 条件4: Input Studio の既存機能と設定は AI／network 障害でも使える — **確認済み**

`InputStudioIsolation.Assess` は AI／network／capture の単独・同時 fault で mapping 編集・profile 保存・mapping 実行を維持し、失敗 dependency を隠さない。未知 dependency は拒否する。fast path／watchdog／dispatch を呼び出さない。通し試験で発見した `InputStudioIsolationTests` の `using Xunit;` 欠落は t10 で直した。focused 5 green。実装 `6f2f654` は祖先。

### 条件5: 各 ToDo focused green＋証跡＋着地。H は未確認のまま残してよい。通し試験は Exit だけ — **確認済み**

| task | feat | 判定 |
|---|---|---|
| t01 | `d51fa6d` | 確認済み |
| t02 | `31288d5` | 確認済み |
| t03 | `3f357c9` | 確認済み |
| t04 | `3d84ff0` | 確認済み |
| t05 | `4fdda5d` | 確認済み |
| t06 | `6f2f654` | 確認済み（通しで見つかった test using 欠落は t10 で修正） |
| t07 | `d8dd417` | 確認済み（WPF 配線は対象外） |
| t08 | `63137b2` | 確認済み（provider 選定と実測は未確認） |
| t09 | `42accaa` | **未確認**（実 game 本体の Verified live なし） |

席は t09／t10 を取っていない。

## H

実 game 用 Verified Step の独立 live session は窓が無い。未確認のまま残す。一般対応と書かない。

## full regression

2026-08-22、`dotnet test OpenLogicool.sln --nologo --maxcpucount:1` を **1回**（Host.Tests の `using Xunit;` 修正の後）。失敗 0。計 **697** 件。

## Grok read-only 監査

親が t01〜t09 の実装を読んだ。公開 claim に Verified を含める口は無い。未確認を Supported にする口は無い。未知 schema version を読み飛ばす口は無い。active Run 中の update 開始口は無い。capability は既存 `GamePolicyGate` と `VerifiedEnvScope` を迂回しない。再起動直後に dispatch を解錠する口は無い。Input Studio 隔離は fast path を待たない。cloud 送信は provider 未選定と screen／secret を開始しない。eval 記録は provider 選定口と評価後の prompt 調整口を持たない。t09 は launcher を本体に読み替えていない。重大な成立取り違えは無い。

## 対象外

- Phase 9（計画正本に無い）
- provider 選定
- Authenticode／clean VM
- G600 leftover の実機確認
- 装飾 UI
