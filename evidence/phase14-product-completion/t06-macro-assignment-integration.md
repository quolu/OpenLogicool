# t06-macro-assignment-integration 証跡

工程正本: Lattice plan `phase14-product-completion` / task `t06-macro-assignment-integration`
担当: koharu（設計・実装） / 記録日: 2026-08-29

## 工程指定（正本の逐語）

> デモ由来macroを既存合成、G13／G600割当、button queue、SQLite再openへ通す。同じmacro tokenとpublic intentsを使い、既存route、正常step、既存device設定を作り直さない。

## 結論

**製品コードは1行も変えていない。** 調査の結果、デモ由来macroはAI由来macroと同じ既存経路をそのまま通ることが分かり、t06の成果は「本当に通ることを実SQLiteで一巡して固定した統合test 5件」である。新しいtokenもpublic intentsも作っていない。

工程指定が「通す」であって「作る」ではないため、通っているものを作り直さないことがこの工程の正しい成果だと判断した。通っていない箇所があれば最小修正する方針で着手し、実際に落ちた箇所は無かった。

## 調査で確認した既存経路（すべて既存・変更なし）

| 段 | 実体 | デモ由来macroの扱い |
| --- | --- | --- |
| catalog | `HostMacroAutomationIntents.ListMacros()` | `GameId == target.ProcessName` で絞る。デモ原本の GameId は `StartAsync` の draft が `MacroTargetSettingsStore` の ProcessName から作るので、AI由来と同じ条件で載る |
| token | `MacroInvocationTokens` / `MacroAssignment.CreateToken` | `Macro:<mode>:<routeId>:<version>`。デモ由来でも同じ生成関数 |
| 割当 | `HostWorkspaceEditorIntents.Compile` / `Save` | action の output に token を置き、G13／G600 の control へ binding。profile は `{WorkspaceId}-{DeviceKind}` |
| 文法検証 | `DeviceMappingRuntime.ValidateOutputGrammar` | macro は単独binding必須。`MappingProfile` は構造だけ見て token 文法を持たない |
| button queue | `FastPathPump` → `MacroInvocationQueue` → `MacroAutomationWorker` | down で1回 enqueue、物理 emitter へは出さない |
| 実行 | `HostMacroAutomationIntents.RunQueuedAsync` → `ExecuteAsync` | t04 が入れた `DemonstrationRecordingGate` を通る（queue経由でも排他が効く） |
| 解決 | `HostMacroCatalog.Resolve` | `MacroVersionReference` から Learning Route revision を引く |
| 合成 | `HostMacroCatalog.Compose` → `MacroRouteComposer` | source route の Structure edge 列が連続していることを要求 |

## 試験内容と試験結果

Windows native（net10.0-windows / .NET SDK 10.0.400）。実SQLite（temp file）を使い、fake は「OS/実機に触る境界」だけに置いた。

### 新規統合test 5件（`tests/OpenLogicool.Host.Tests/DemonstrationMacroAssignmentScenarioTests.cs`）

| 試験 | 確認した内容 |
| --- | --- |
| `A_demonstration_macro_is_listed_assigned_to_both_devices_and_survives_reopen` | 記録→`CreateMacroFromSession`→`ListMacros()` に GameId=game・Goal=アークを開く で載る→`MacroAssignment.CreateToken`→G13 `G1` と G600 `G9` へ割当→compile が valid で profile 2件→保存→**別connectionで再open**して workspace revision と profile を読み直すと、同じ token が `(G1, base)` と `(G9, base)` に入っている |
| `Pressing_the_assigned_button_puts_the_demonstration_macro_into_the_button_queue` | 再open した profile から `DeviceMappingRuntime` を組み、G13/G600 の割当 control を down/up。`FastPathPump.RunOnce()` が4入力を処理し、accepted 2・rejected 0・**物理 emitter への出力0**。queue から取り出した2件が割当 token と完全一致し、`HostMacroCatalog.Resolve` がデモ原本から作った route（RouteId・Goal 一致・edge 非空）を返す |
| `A_demonstration_macro_can_be_composed_with_another_route_without_rebuilding_it` | デモ由来 route の終点から続く区間を既存形式（structure edge＋Learning Route）で足し、`Compose` に source として渡す。合成 route の edge 列が「デモ由来 edge＋続きの edge」の順で一致。**デモ由来 route は revision が増えず edge 列も変わらない** |
| `Assigning_a_demonstration_macro_does_not_rebuild_the_route_or_the_existing_binding` | 既にキー割当（`Key:F13` → G13 `G2`）を持つ workspace へ後からデモ由来macroを足しても、元の action と binding がそのまま残り、デモ由来 route の revision 数・番号・edge 列が不変 |
| `A_queued_button_press_is_refused_while_a_demonstration_is_recording` | 記録器と再生intentsが同じ `DemonstrationRecordingGate` を持つとき、`MacroAutomationWorker` が queue から取り出して呼ぶ経路（`IMacroInvocationRunner.RunQueuedAsync`）が記録中は拒否され、実行engineは0回。停止後は同じ呼び出しが通り1回実行 |

実行結果:
```
DemonstrationMacroAssignmentScenario 成功! 失敗: 0、合格: 5、合計: 5
```

### 関連test（module単位・最終確認）

```
Host 287 / Playbooks 194 / Input 160 / Profiles 25 / Persistence 55
Desktop 103 / Architecture 8 / Exploration 64      合計 896件 全green
solution build 警告0・エラー0
```

Architecture test が green なので依存行列は壊れていない。full regression はこの工程では実行していない（t09の範囲）。

## 途中で当たった事実（記録）

**合成は Structure edge 列が連続していることを要求する。** 最初、同じ画面から始まる別々のデモ2本を合成しようとして `Structure edge列が連続していません。` で落ちた。原因を追うと `MacroRouteComposer.Compose` → `LearningRouteValidator.Validate` → `StructurePlaybookSynthesizer.Synthesize` の既存検証で、**デモ由来かどうかとは無関係にAI由来routeでも同じ**である。合成できる組み合わせは「前の route の終点から次の route が始まる」ものだけで、これは仕様どおりの正しい拒否なので製品側は変えず、test を「続きの区間を用意して合成する」形にした。

切り分けの過程で、structure の node 同定が `GameSceneSemanticComparer.SignatureId`（StateIdentity＋StateCandidate の StateId＋affordance の意味キー）による内容一致で行われ、`GameInteractionStructureLearner.EnsureNode` が既存 node を再利用することも確認した。落ちた原因は fake が出す scene の内容差であって、同定機構の欠陥ではない。

## このtaskで作らなかったもの（範囲外・正直な残課題）

- **`Program.cs` の合成点で記録器と再生intentsに同じ gate を渡す配線はしていない。** `HostDemonstrationRecordingIntents` は現時点で製品の合成点（`Program.cs`）にまだ登場せず（t04 は intents の契約と実装まで、t05 は Desktop 側）、共有すべき相手の instance が存在しない。gate を共有したときに button queue 経由の再生が止まることは上表の5件目で固定してあるので、配線が入った時点で成立する。配線そのものは t07（product journey acceptance）の範囲と判断した。
- 実機 G13／G600 を押しての live 確認はしていない。fast path は fake device source で駆動している（オーナーのNIKKEプレイ中につき前面・入力へ触れない方針 room #1049 / #1051 を継続）。
- 製品コードを変えていないので、既存 route・正常 step・既存 device 設定に対する変更も無い。

## 4値（この工程の範囲）

- デモ由来macroが既存catalogへ載る: **確認済み**（統合test。GameId は記録時の対象選択と同じ）
- 同じmacro tokenを使う: **確認済み**（`MacroAssignment.CreateToken` の1経路だけ。新token無し）
- G13／G600の両方へ割当できる: **確認済み**（compile valid・profile 2件・再open後も両方に token）
- button queueへ通る: **確認済み**（down 1回で1件 enqueue・物理emitterへ0・queueの参照がデモ由来routeへ解決）
- SQLite再openで残る: **確認済み**（別connectionで workspace revision と mapping profile を読み直して一致）
- 既存合成へ通る: **確認済み**（連続する区間を持つ組み合わせで合成成立・デモ由来routeは不変）
- 既存route・正常step・既存device設定を作り直さない: **確認済み**（revision数／番号／edge列とworkspaceの既存action・bindingが不変。製品コード変更0）
- 記録中はbutton queue経由の再生も止まる: **確認済み**（gate共有時。ただし製品の合成点での gate 共有配線は未実施＝下記）
- 製品の合成点で記録器と再生が同じgateを持つこと: **未確認**（配線先がまだ存在しない。t07の範囲）
- 実機G13／G600押下でのlive確認: **未確認**（前面・入力へ触れない方針を継続）

## 変更file

- `tests/OpenLogicool.Host.Tests/DemonstrationMacroAssignmentScenarioTests.cs`（新規・統合test 5件）
- `evidence/phase14-product-completion/t06-macro-assignment-integration.md`（本書）

製品コード（`src/`）の変更は無い。
