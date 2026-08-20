# Phase 6 Exit Assessment（AI Teach／Learn）

- 作成: 2026-08-20（統括 bell-grok46・t08）
- 上位正本: [development-plan.md](development-plan.md) §Phase 6
- campaign 受入: [phase6-campaign-plan.md](phase6-campaign-plan.md)
- 判定材料: Lattice plan `phase6-ai-teach`（t01〜t07 done・t08 は本書）・[evidence/phase6-ai-teach/](../evidence/phase6-ai-teach/)
- 根拠4値: 確認済み／強い推定／未確認／非対応
- Exit 宣言: **2026-08-20 親（bell-grok46）**。技術成立の宣言でオーナーに止めない。provider は未選定のまま閉じる

## Exit 8条件の判定

### 条件1: AI が direct input／DB／device API へ到達できない — **確認済み**

- 構造: `OpenLogicool.AI.csproj` の ProjectReference は `OpenLogicool.Contracts` だけ。`ProjectReferenceDirectionTests` が AI → Input／Devices.G13／Devices.G600／Persistence／Capture／Playbooks を拒否する。
- 公開口は `INextActionPlanner`／`NextActionProposal`。EvalHarness・ObserveOnly・TeachSupervised は proposal を返すか承認待ち値を返すだけで、SendInput／HID／SQLite を呼ばない。
- 実測: Architecture focused 5 green。2026-08-20 の full regression に含まれる。

### 条件2: schema 外、catalog 外、state 不一致、risk 不一致 proposal を dispatch 前に拒否 — **確認済み**

- 構造: `ProposalReject.Evaluate` は判定結果だけを返す。InputEmitter も dispatch delegate も持たない。schema は `PlannerProposalSchema.Validate`、catalog／state／risk は catalog と precondition の ordinal 照合。
- 実測: `ProposalRejectTests` focused green。実装 `346a6cb` は origin/main 祖先。

### 条件3: 初見 GameLab scenario を途中保存し、別 session で既知を replay、未知だけ追記できる口がある — **確認済み**

- 途中保存／別 session replay: `SessionRecorder.Restore` は store の実 event だけから projection を再生する。`SessionReplayer` は読み取り専用。Phase 4 で focused 実証済みで、本 HEAD に残っている。
- 未知の追記口: `TeachSupervised` が Teach proposal を `PendingTeachStep` に留め、明示 `approvalId` のときだけ `ApprovedTeachStep` にする。`PlaybookCorrection.Revise` は旧 Version を書き換えず新 Version を返す。
- Teach 口は Playbook を直接書かない。追記の永続化は既存 journal／correction 境界が担う。口が無い状態ではない。

### 条件4: GameLab の Verified が実 game へ継承されない — **確認済み**

- 構造: `VerifiedEnvScope.AppliesTo` は environment ID の ordinal 完全一致だけ。`gamelab:scenario-01` は `game:nikke` に適用しない。
- 実測: `VerifiedEnvScopeTests` 2 green。実装 `10ef50e` は origin/main 祖先。

### 条件5: acceptance dataset を prompt 調整へ使っていない — **確認済み**

- 構造: `EvalHarness` に prompt／model／dataset 調整 API が無い。入力は凍結 corpus item と注入された `IFrozenProposalEvaluator`。src/OpenLogicool.AI に `prompt` 識別子は無い。

### 条件6: provider 停止時も Input Studio と verified deterministic Playbook が使える — **確認済み**

- 構造: fast path（Device Input → Mapping Runtime → Input Emitter）は AI を待たない。Host の Input Studio 経路と Playbooks の journal／gate は AI プロジェクトを参照しない。
- provider client は製品に埋め込まれていない。停止する対象が無い。

### 条件7: EXP-AI-01 は比較 harness まで。provider を選定しない — **確認済み**

- `EvalHarness.Measure` は精度・unknown 棄却・latency・cost・cancel を集計する。本番 provider slug／credential を持たない。
- 実装 `ee35910` は origin/main 祖先。focused AI.Tests 2 green。

### 条件8: 各 ToDo focused green＋証跡＋着地。通し試験は Exit だけ — **確認済み**

| task | feat | 証跡 | focused |
|---|---|---|---|
| t01 | `4a108e9` | [t01-planner-proposal-schema.md](../evidence/phase6-ai-teach/t01-planner-proposal-schema.md) | Conformance／schema |
| t02 | `a3e2371` | [t02-ai-isolation.md](../evidence/phase6-ai-teach/t02-ai-isolation.md) | Architecture 5 |
| t03 | `346a6cb` | [t03-proposal-reject.md](../evidence/phase6-ai-teach/t03-proposal-reject.md) | ProposalReject |
| t04 | `ee35910` | [t04-exp-ai-01-harness.md](../evidence/phase6-ai-teach/t04-exp-ai-01-harness.md) | AI.Tests 2 |
| t05 | `4a1f8e3` | [t05-observe-only.md](../evidence/phase6-ai-teach/t05-observe-only.md) | ObserveOnly 2 |
| t06 | `6de0dfd` | [t06-teach-supervised.md](../evidence/phase6-ai-teach/t06-teach-supervised.md) | TeachSupervised 2 |
| t07 | `10ef50e` | [t07-verified-env-scope.md](../evidence/phase6-ai-teach/t07-verified-env-scope.md) | VerifiedEnvScope 2 |

全 feat は origin/main の祖先。席は t08 を取っていない。

## Exit で直した欠陥

t04 の `tests/OpenLogicool.AI.Tests` が solution に載っておらず、最初の `dotnet test OpenLogicool.sln` から外れていた。solution へ追加し focused 2 green のあと、通し試験を 1 回やり直した。黙って「sln に無いから対象外」へは落とさない。

## full regression

2026-08-20、HEAD（t07 着地＋ AI.Tests を sln へ追加した作業木）で `dotnet test OpenLogicool.sln` を **1回**。失敗 0。計 **628** 件。

| プロジェクト | 件数 |
|---|---|
| Architecture | 5 |
| AI | 2 |
| GameLab.Prototype | 3 |
| Desktop.SmokeApp | 2 |
| Domain | 90 |
| Playbooks | 117 |
| Input | 79 |
| Profiles | 22 |
| Desktop | 58 |
| Perception | 16 |
| GameLab | 23 |
| Capture | 16 |
| Conformance | 27 |
| Devices.G13 | 10 |
| Devices.G600 | 59 |
| Probe | 6 |
| Persistence | 29 |
| Capture.Matrix | 6 |
| Host | 58 |

## Grok read-only 監査

親（Grok 4.6）が t01〜t07 の実装と証跡を読んだ。SendInput／HID／SQLite を AI 経路から呼ぶ口は無い。provider 選定はしていない。重大な成立取り違えは無い。

## 対象外

- 有料 provider の本番課金、cloud へ実 game 画像を送る consent（campaign H）
- G600 残置の実機確認
- peertable MS-A2 room 入れ替え（本 campaign の製品面ではない）
