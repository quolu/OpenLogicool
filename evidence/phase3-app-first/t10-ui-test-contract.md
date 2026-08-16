# t10 実測証跡（2026-08-16）: UI test scenario の fake/real contract 一致

Exit 条件5（Phase 3・[docs/development-plan.md](../../docs/development-plan.md) §Phase 3 Exit）:
「UI test scenario が fake と real contract で同じ結果になる」の実証。

## 構成

- scenario 定義: `src/OpenLogicool.Desktop/UiTestScenario.cs`（`UiTestScenario.Run`）。
  `InputStudioWindow` のイベントハンドラが呼ぶのと同じ public 経路
  （`IWorkspaceEditorIntents.LoadDocument/Compile/Save`・`WorkspaceDocumentEditor.AddAction/SetBinding/SetActionOutputs`・
  `WorkspaceScreenProjection.Project`）だけを呼ぶ pure runner。テスト専用の別経路は作っていない。
- fake: `src/OpenLogicool.Host/FakeWorkspaceEditorIntents.cs`（in-memory storage。compile・保存前解決可能性検証は
  real と同一の共有関数 `WorkspaceCompiler.Compile`・`AppProfileResolver.Build`・`WorkspaceEditorIntentsSupport` を呼ぶ）。
- real: `src/OpenLogicool.Host/HostWorkspaceEditorIntents.cs`（新規 temp SQLite・実 device 列挙）。
  fake/real 双方が使う分岐ロジック（`ValidateOutputTokens`・`TryReverseWorkspaceId`・`ProposeWorkspaceId`・`BuildStages`）は
  `WorkspaceEditorIntentsSupport.cs` へ抽出し、二重実装を排した（extract method・挙動変更なし。既存 Host.Tests 全 green で確認）。
- 突き合わせ: `src/OpenLogicool.Host/UiTestScenarioComparer.cs`（field 単位で機械判定）。
- 実行経路: `OpenLogicool.Host.exe ui-test-scenario [--out <path>]`（fake・real 双方を同一 process 内で実行し比較・
  JSON 証跡を `probe-output/` へ書く。常駐 host が動いていれば前提違反として拒否——`run`／`ui --resident` は事前に停止する）。

## scenario 各段

1. **アプリ選択（共通設定→特定アプリ）**: `"*"`（共通設定）を `LoadDocument` → 続けて対象アプリ
   `c:\game\t10-scenario-app.exe`（実行中でも関連付け済みでもない合成 path）を `LoadDocument`。
   `WorkspaceScreenProjection.Project` で編集対象ラベルが「共通設定（どのアプリでもない時）」→「t10 シナリオアプリ」へ動くことを確認。
2. **操作作成**: `WorkspaceDocumentEditor.AddAction`（`action-t10` / 「t10 テスト操作」）。
3. **G13/G600 両 device binding**: `SetBinding` で G13 の確認済み control `G1`、G600 の確認済み control `G9`（いずれも base layer）へ割当。
   出力 token は `SetActionOutputs` で `Key:A`。
4. **保存**: `intents.Save(document)` → revision 番号・段階セル（`WorkspaceApplyReport` 語彙）を取得。
5. **適用状態表示の確認**: 保存後 snapshot を `WorkspaceScreenProjection.Project` に通し、
   `AppliedRevisionLabel`（"revision N"）・段階セル・device 接続要約を取得。

## 一致項目（全て機械判定・`UiTestScenarioComparer`）

`DeviceConnectionLabels` を除く全 field（アプリ選択ラベル・action 数/ID/名前/出力・G13/G600 binding・
compile 成否/警告/profile数・save revision番号・段階セル・保存後の編集対象ラベルと適用 revision ラベル）。

## 除外項目と理由

- `DeviceConnectionLabels`（例: 「G13: 接続中（1 台）」）: 実機接続台数の列挙結果に依存する環境値。
  fake は固定値（G13=1・G600=1）、real は `G13RawInputSource`/`G600RawInputSource` の実列挙結果を使う。
  除外理由は `UiTestScenarioComparer.ExcludedFields` として証跡 JSON にも明示している。

## 実測結果

`OpenLogicool.Host.exe ui-test-scenario` を実機（G13・G600 両接続、常駐 host 停止状態）で実行。

- **判定: 一致（IsMatch = true・不一致 0件）**
- real 側の実機列挙: G13=1台・G600=1台（除外項目だが偶然 fake の固定値と同じ値になった——除外の判定自体は
  値の一致に依らず field 名で機械的に行っている）
- compile: 両方とも成立・profile 2件（G13/G600）・警告3件（既定 draft layout の G13 M1/M2/M3 latch selector が
  「強い推定」control のため——binding 先に選んだ G1/G9 はどちらも確認済みで警告対象外）
- save: 両方とも revision 1・段階セル「編集（compile）=成立」「保存（revision）=成立」
  「runtime 適用=未適用（host 非常駐）」「device 反映=対象外」

証跡 JSON: [probe-output/ui-test-scenario-20260816-124626-874.json](../../probe-output/ui-test-scenario-20260816-124626-874.json)

## focused test（常設）

`tests/OpenLogicool.Host.Tests/UiTestScenarioTests.cs`（fake 単独で scenario の各段を検証・9件）
+ 既存 Desktop.Tests 58件・Host.Tests 47件（うち新規9件）・Profiles.Tests 22件・Persistence.Tests 16件・
Architecture.Tests 4件、すべて green。real 側は CI 常設せず、本 CLI 実行による証跡化だけを正とする
（development-plan.md §7.1 Lane A「core journey を fake と real contract で再利用」の運用どおり）。

## 未検証範囲・前提の食い違い

- real 側の「常駐 host が動いていない」前提は起動時に `WorkspaceRevisionSaver.IsHostResident()` で検査しているが、
  この検査自体は CLI 実行時点の named mutex 観測であり、実行途中で別 process が常駐を開始するレースは対象外
  （通常運用では起こらない前提で許容）。
- rail（ApplicationRail）の行構成は scenario 内で fake/real 共通の literal を使っており、
  `ApplicationWorkspaceCatalog.Build`＋実行中 app 一覧（`RunningApplicationCatalog`）を経由する本番の rail 構築
  （`Program.cs` の `BuildRailEntries`）そのものは今回の scenario の比較対象に含めていない
  ——rail 構築は表示専用の関心であり、この t10 が検証する「fake/real の storage 境界の contract 一致」の対象外と判断した。
