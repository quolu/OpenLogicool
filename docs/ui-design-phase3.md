# Input Studio Desktop UI 設計（Phase 3・t08 確定版）

- 起草: Grok 4.6×high への設計相談（2026-08-16・オーナー指名の相談席・repo read-only 実読済み）
- 統括裁定: 全文採用（案A＋固定提案9項。特に 8=`ui --resident` と 9=保存後の RequestProfileChange 追従も採用）。t09 実装はこの文書を仕様正本とする
- 上位正本: development-plan.md §5。矛盾したら計画側が正

現行骨格は「device ごとの能力台帳」で、Journey B（アプリを選ぶ→action を作る→両機へ割当）の作業面になっていません。以下は、実装済みの `WorkspaceDocument` / `WorkspaceCompiler` / `WorkspaceApplyReport` を皮にする前提の設計です。

---

# Input Studio Desktop UI 設計相談（t08）

対象: Phase 3 Exit（app を一度選び device 別画面を往復せず両機を設定／Alt+Tab で編集対象を失わない／片側接続でも完結／UI scenario が fake と real で同じ結果）。  
非対象: Phase 4 以降の Automation／Timeline／AI。装飾より情報設計。

## 0. 現行骨格が使えない理由

`InputStudioWindow` は G13 節→G600 節の縦積みで、各節が接続／所有／profile／制約／全 control の `DataGrid` です。これは Phase 2 Exit 条件1・4（欠落なく表示・変換）は満たしますが、次を満たしません。

- 編集単位が Application Workspace ではなく device
- 「編集中 app」と「現在有効 app」が無い（APP-002、Alt+Tab 条件）
- Action 行が主役でない（§5.2）
- 保存／runtime／device の段階が無い（APP-007）
- 色が `Brushes.DimGray` 固定で high contrast を壊す

残すもの: `InputStudioReportBuilder` の根拠4値変換と全 control 列挙。行き先は **Diagnostics の device 台帳** であり、初画面ではない。

---

## 1. 画面構成（3案）

いずれも最上位は Applications → Workspace。Hardware Maintenance は通常 flow に出さない。

### 案A — Workspace Command Surface（推し）

1画面に「識別ヘッダ＋段階ストリップ＋app レール＋Action 盤＋両機シルエット＋同一 inspector＋test field」。ページ遷移なし。

```
┌──────────────────────────────────────────────────────────────────────────┐
│ 編集中: NIKKE          現在有効: OpenLogicool.Host     対象: nikke.exe   │
│ 状態: 一致 app（path）  適用 rev 5            実行: 手動入力            │
│ 編集 成立 │ 保存 下書き │ runtime 未反映 │ device 対象外                │
├────────┬─────────────────────────────────────────────┬───────────────────┤
│ アプリ  │ Action                              G13     │ 割当 inspector    │
│ ●既定   │ 回避    Key:LShift     G7 / —      G12/G16 │ action: 回避      │
│ ●NIKKE │ スキル  Key:E          G8 / M2     — / G9  │ 出力: Key:LShift  │
│  explorer│ ＋ action                              │ G13 G7・通常       │
│ [実行中]│                                             │ G600 G12・通常    │
│         │ ┌ G13 図 ┐  ┌ G600 図 ┐                    │ [このキーへ割当]  │
│         │ │G1..G22│  │G1..G20 │  層チップ: 通常/M2 │                   │
├────────┴─────────────────────────────────────────────┴───────────────────┤
│ test field  G600 G12 down  layer=base  → Key:LShift  emitted  #1842     │
└──────────────────────────────────────────────────────────────────────────┘
```

満たすもの: §5.2 の header 5項目、Actions 主役、図からも一覧からも同じ inspector、保存＝revision、部分成功を統合成功に見せない。片側未接続は該当列／図を「未接続（編集は可）」にして残す。

### 案B — LGS 近い分割（Overview / Actions / Layout タブ）

§5.1 の木をタブにする。情報は正しいが、Journey B がタブ往復になる。LGS の「キーを選んでマクロを付ける」に引き戻され、Action-centric が崩れる。**不採用**（Phase 3 Exit の「往復せず」に負ける）。

### 案C — App ピッカー先行＋ワークスペースを別ウィンドウ

編集対象の固定はしやすい。常時表示（APP-002）と「今どの app の入力が生きているか」が別窓になり、Alt+Tab 条件の説明コストが上がる。2ウィンドウは WPF 実装もテストも増える。**不採用**。

### 比較

| 軸 | 案A | 案B | 案C |
|---|---|---|---|
| Journey B が1画面で完結 | する | しない | ピッカー往復が残る |
| 編集対象 vs 有効 app | 常時同じヘッダ | Overview に隠れやすい | 窓をまたぐ |
| LGS 再発明リスク | 低い | 高い | 中 |
| 片側接続 | 列を畳むだけ | タブが空になる | 同様 |
| t09 の実装単位 | シェル→盤→inspector | ナビ基盤が先に膨らむ | 窓管理が先に膨らむ |

**推しは案A。** 製品の主張は「意味操作を両機へ同時に載せる」であり、画面の主役は Action 盤であるべきです。device 図は選ぶための地図であり、編集の主面ではありません。

### ヘッダの中身（案A固定）

| 欄 | 出す値 | 出さない値 |
|---|---|---|
| 編集中 | 利用者が選んだ workspace（既定 or 関連付け app） | foreground の自動追従 |
| 現在有効 | `ForegroundStateClassifier` の対象（path / package / 既定） | 「たぶんこのゲーム」 |
| 対象 window | タイトル＋ identity の有無。3状態は文言で出す（一致／既定／Unknown） | 色だけの丸 |
| 適用 revision | 保存済み番号。未保存は「下書き」 | 「適用済み」の単一ランプ |
| 実行 mode | Phase 3 は **「手動入力」固定表示** | Observe/Teach の偽ピッカー |

段階ストリップは `WorkspaceApplyReport` の4段をそのまま出す。語彙は既存どおり「成立／未実施／未反映／未適用／対象外」。緑の一括チェックは置かない。device 段は MAP-010 どおり常に「対象外」。

---

## 2. Binding editor の操作フロー

Journey B を1画面の状態遷移にする。画面は切り替えず、選択と inspector の中身だけが変わる。

### 2.1 一つの作業の通し

1. **アプリを選ぶ**  
   左レールは `ApplicationWorkspaceCatalog` の行＋`RunningApplicationCatalog` の実行中一覧。関連付け path は実行中 process から取った値だけを正とする（Store メモ帳の redirect 罠）。手打ち EXE は置かない。実行中に無い app は「その app を前面にしてから選ぶ」。  
   選んだ瞬間に変わるのは **編集中** だけ。現在有効は foreground tracker の観測値のまま。これが Alt+Tab 条件の構造です。Input Studio 自身が前面になっても編集中は NIKKE のまま。

2. **Action を作る**  
   盤の「＋ action」。名前（「回避」）と出力（`Key:LShift` 等）。出力はピッカーが token を書き、**canonical な token 文字列を常に見せる**。未知 token は `OutputTokens.Parse` が例外にするので、UI は握りつぶさず「この出力は送れません」と出す。  
   未割当のまま作ってよい。compiler の Warning「未割当」が盤とストリップに出る。

3. **両 device へ割当（同じ inspector）**  
   入口は2つ、出口は1つ。
   - 盤の行（または G13／G600 セル）をクリック → inspector がその action を開く。両図の該当キーが同時に強調される。
   - 図のキーをクリック → 同じ inspector が「その (device, control, 表示中 layer)」を開く。既存 binding があればその action を選択。無ければ選択中 action を割り当てる／新規作成。

   層は **別ページにしない**。各図の上に層チップ（G13: 通常 / M2 / M3、G600: 通常 / G-Shift）。チップは図のフィルタであり、盤は全層を列で見せ続ける。selector（M1/M2/M3 latch、G6 hold）は inspector の「このキーの役割」で切り替える。selector にしたキーは binding を持たない（現行 `Role` 表示と同じ）。

4. **検証**  
   変更のたび Host が `WorkspaceCompiler.Compile` を呼ぶ（pure、UI スレッドで可）。
   - 例外（衝突全列挙・未知 action・layer 不整合）→ 保存ボタン無効。「衝突: (G600, G12, base) に '回避', 'スキル'」を盤と inspector の両方に出す。
   - Warning（未割当・到達不能 layer・未確認 control）→ 保存はできる。Experimental は「強い推定」と書き、Supported にしない。

5. **適用＝保存**  
   主ボタンは **「保存（revision）」** だけ。ラベルに「適用完了」を使わない。保存は既存どおり revision 追記＋両 profile upsert の単一 transaction。片側失敗は起きない（起きても一括成功にしない）。  
   保存後の段階:
   - 保存: 成立（rev N）
   - runtime: 常駐中なら「未反映（host 再起動が必要）」、非常駐なら「未適用（次回起動時）」
   - device: 対象外

   「再起動して反映」は別アクション。保存成功を runtime 成功にしない。

6. **game に戻って確認**  
   下の test field が `InputTraceEntry`（device / control / edge / layer / outputs / emitted）を出す。常駐が居ないときは「入力確認は常駐起動後」。trace は fast path を待たせない（既存 `DrainTrace` を UI が引く）。

### 2.2 迷う点

| 点 | 案 | 推し |
|---|---|---|
| inspector の置き場 | 右ペイン常時 / 下部 / モーダル | **右ペイン常時**。モーダルは「図と一覧が同じ editor」を壊す |
| 図→割当 | ドラッグ / クリックで割当 / 「割当」ボタン後にキー | **選択中 action ＋キークリック**。ドラッグはキーボード不能なので Phase 3 ではやらない |
| 層の見せ方 | LGS 風モード画面 / 図のチップ＋盤の列 | **チップ＋列**。モード画面は往復そのもの |
| 出力入力 | 生 token のみ / ピッカーのみ | **ピッカーが token を書き、文字列を正本表示** |

### 2.3 現在有効を編集中にする

ヘッダの「現在有効」はクリックできる。確認のあと編集対象をそれに切り替える。自動追従はしない。Input Studio 前面中に編集対象がゲームから Host へ消える、というのが今回いちばん安い失敗です。

---

## 3. WPF 実装の構成

### 3.1 層と参照（動かさない契約）

architecture test が Desktop の参照を **Contracts + Domain だけ** に固定している。Persistence / Devices / Input / Profiles / Host は禁止。これを Phase 3 で崩さない。

含意:

- Desktop は I/O を持たない。SQLite・Raw Input・SendInput を呼ばない。
- `WorkspaceCompiler` / `WorkspaceApplyReport` / `WorkspaceUndo` は Host が呼び、結果の snapshot を Desktop に渡す。
- fast path は今どおり専用 worker。UI は `DrainTrace` 相当を **pull** するだけ。
- Desktop → Profiles を足す案は、live compile が Host 往復で苦しくなってから。最初から参照を増やさない。

### 3.2 MVVM の要否

**フル MVVM フレームワークは不要。** Prism / Caliburn / CommunityToolkit.Mvvm は入れない。画面は実質1つ、状態は `WorkspaceDocument` と数個の観測 snapshot。

採用するのは現行と同じ **pure builder ＋薄い View**:

| 層 | 置き場 | 役割 |
|---|---|---|
| Snapshot / Projection | `OpenLogicool.Desktop`（pure、今日の `InputStudioReportBuilder` の続き） | Workspace + 接続数 + foreground + 段階 + warnings → 表示モデル |
| Intent | Desktop の delegate / interface（実装は Host） | 保存・undo・関連付け・実行中再取得 |
| View | `Window` + `UserControl`、code-behind は入力イベントだけ | 描画と focus |
| ライブ値 | 少数の `INotifyPropertyChanged`（foreground、trace 最終行、段階） | 200ms poll の結果を映す |

テストは Projection を fake snapshot で行う。これが Exit「fake と real contract で同じ結果」の実装形。WPF コントロールのクリックを E2E しない。

### 3.3 画面／コンポーネント分割

```
InputStudioWindow            シェル（メニュー: 保存/undo/export/診断/初回）
  WorkspaceChrome            ヘッダ5欄＋段階4セル
  ApplicationRail            既定＋関連付け＋実行中。選択＝編集対象
  ActionBoard                主役。行=action、列=出力 / G13層… / G600層…
  DeviceFigureView ×2        模式図。キーは Button。未確認は破線＋「強い推定」
  BindingInspector           唯一の editor。図からも盤からも同じ instance
  TestFieldDock              DrainTrace の最終 N 件。常駐なしは理由を書く
  OnboardingOverlay          Journey A。初回またはメニュー
  DiagnosticsWindow          現行 InputStudioReport の DataGrid＋制約全文
```

Hardware Maintenance（照明／LCD／DPI／onboard／raw report）は Diagnostics 配下の見出しだけ先に置き、中身は「未実装（Phase 8A / 保守）」と書く。空の編集画面は作らない。

### 3.4 Host 側の合成

今: `host ui` は列挙して report を渡し、fast path を起動しない。

提案:

| 起動 | 動き |
|---|---|
| `host ui` | 編集シェル。保存・undo・関連付けはする。test field は「常駐なし」 |
| `host ui --resident`（新規） | 同一 process で STA UI ＋既存 `ResidentInputHost`。trace を drain。mutex は1つ |
| 常駐中の二度目の `ui` | 今どおり二重起動拒否。tray 転送は Phase 3 ではやらない |

UI スレッドから fast path を待たない。保存後に runtime を即座に切り替えるなら、既存 `FastPathPump.RequestProfileChange` を Host が呼ぶ（device write なし）。これは UI ではなく Host の小さな追加。やらなければ段階表示は今の「未反映（再起動）」のまま——嘘にはならない。使いやすさのため **保存成功後に resident へ RequestProfileChange する** のを推奨する。やらなければ UI が「保存したのにキーが古い」に見える。

### 3.5 現行骨格からの段階

既存 `InputStudioWindow` を一度に捨てない。

1. **シェル置換**  
   新 `InputStudioWindow` に Chrome + 空の盤 + 右に現行 device DataGrid を暫定配置。`InputStudioReportBuilder` テストは残す。これで「app-first の枠」だけ先に目視できる。

2. **編集対象の固定**  
   ApplicationRail + Chrome の編集中／現在有効。foreground poll は Host。focused test: Alt+Tab 相当の fake identity 変更で編集中が動かない。

3. **Action 盤 + Inspector**  
   `WorkspaceDocument` の読み書き。compiler 結果の警告／衝突。保存ボタンが `WorkspaceApplyReport` を更新。CLI `workspace` / `undo` / `export` と同じ port。

4. **模式図を同じ inspector へ接続**  
   図は後回しでよい。盤だけで Journey B は完結する。図は「同じ editor」契約の第二入口。

5. **test field / onboarding / diagnostics 分離**  
   現行 DataGrid を DiagnosticsWindow へ移設。初回は OnboardingReport の行を overlay に。

6. **UX-007 の通し**  
   キーボード・high contrast・125/150/200%。機能が揃ってから。先にテーマを作り込まない。

各段のテスト対象は Projection と「Host が渡す snapshot の形」。Window のスクショは受入材料にしない。

### 3.6 依存を増やさないための境界

Desktop が知りすぎないよう、Host が渡す入力は例えば次で足りる。

- `WorkspaceDocument`（編集中）
- `IReadOnlyList<WorkspaceStageStatus>`
- `IReadOnlyList<string> CompileWarnings` と、衝突時は例外メッセージ
- 接続数（G13/G600）
- `ForegroundState` + path/package/window タイトル
- 実行中 app 一覧（Host が `RunningApplicationCatalog` を呼んだ結果）
- `InputTraceEntry` の drain 結果

Desktop が返す intent: 編集中 workspace の選択、document 置換（action 追加・binding 変更）、保存、undo（番号任意）、export path、実行中再取得、現在有効を編集中にする。

---

## 4. アクセシビリティ（UX-007）

方針は「システムに乗る」。独自テーマで勝てない。

### Keyboard

主な流れをマウスなしで完了する。

| キー | 動作 |
|---|---|
| Tab / Shift+Tab | レール → 盤 → 図 → inspector → 保存、の順 |
| Ctrl+S | 保存（revision）。衝突中は動かず focus を警告へ |
| Ctrl+Z | 直前 revision へ undo（既存「無指定 undo」。連続は打ち消し） |
| Ctrl+Shift+Z | revision 番号を聞く（既存の番号指定） |
| F2 | 選択 action の名前 |
| Enter | inspector の「このキーへ割当」 |
| Delete | 選択 binding または未保存 action |
| 1 / 2 / 3 | 表示層チップ（通常 / 次層 / その次） |

図のキーは `Button` にして Tab 対象にする。Canvas 上のヒットテストだけにはしない。盤は `DataGrid` か `ListView` を使い、自前仮想化キャンバスにしない。

### 色だけにしない

段階・ForegroundState・capability は **必ず文言**（成立／未反映／一致 app／Unknown Application／Supported／Experimental）。色は補助。現行 report の語彙をそのまま使う。

### High contrast

`Brushes.DimGray` / 固定 RGB をやめる。`SystemColors` と `DynamicResource`。フォーカスは `SystemColors.HighlightBrush` の太い枠。high contrast テーマで図の「選択」と「割当済み」が区別できること。

### Display scaling

レイアウトは DIP。図は `Viewbox`。980×760 固定をやめ、最小幅だけ決める（例: 1100×720）。125% / 150% / 200% で盤の列が隠れず横スクロールに逃げる。文字を画像に埋め込まない。

### その他

- `AutomationProperties.Name` をキー（「G600 G12」）と段階セルに付ける
- `TextFormattingMode` は Display。アニメーションなし（reduced motion 以前に動かさない）
- タッチ／ゲームパッド操作は Phase 3 対象外

---

## 5. やらないこと（Phase 3）

機能中核は既にある。UI は皮であり、ここで新しい能力を足さない。

| 入れない | 行き先 |
|---|---|
| Automation / Timeline / Teach / Observe / 実行 mode 切替 | Phase 4〜。ヘッダは「手動入力」固定 |
| G13 lighting / LCD applet / ジョイスティックをマウス化 | Hardware Maintenance。見出しだけ |
| G600 DPI / レポートレート / LED / onboard slot 編集 / side remap 残置 | 同上。write は MAP-010 維持 |
| advanced HID / raw report / feature dump | Diagnostics |
| LGS import（APP-009） | Phase 8A |
| インストール済み一覧・手打ち EXE | APP-001 の一部だが、path の正は実行中。今やると Store redirect を再導入する |
| stick アナログ・ホイール tick の binding | 入力は実測済み、割当対象外のまま |
| timed / repeat / toggle macro | MAP-007 R5 |
| tray・既存 instance への activation 転送 | 骨格コメントどおり後回し |
| 見た目テーマ、ダークモード、RGB 装飾、キーの写真 | 模式図＋システム色 |
| MVVM フレームワーク、DI コンテナ | Host が合成する |
| Desktop から Persistence / Devices / Input を参照 | architecture 契約 |
| 未確認 control を Supported に見せる | 根拠4値 |
| 保存成功を「適用完了」と出す / device 対象外を成功ランプにする | APP-007 |
| 編集対象を foreground に自動追従する | Exit「Alt+Tab で失わない」の否定 |

Diagnostics に移す現行情報（制約全文、F6 read 不能、elevated/UIPI、B変種、F0 slot）は消さない。通常の設定面の第一画面に置かない。

---

## 6. t09 が迷わず実装するための固定提案

実装時に再議論しないための推し。

1. **画面は案Aの1枚。** タブで §5.1 を再現しない。Automation / Timeline は殻も作らない。
2. **Binding editor は右ペイン1個。** 図と盤は選択ソース。
3. **保存ボタンは一つ。** 適用完了という語を UI に出さない。
4. **関連付けの入力は実行中一覧だけ。**
5. **Desktop の参照は Contracts + Domain のまま。** compile / 保存 / 列挙は Host。
6. **フレームワークなし。** Projection テストで Exit 条件5を取る。
7. **`InputStudioReportBuilder` は Diagnostics へ移してテストを維持。**
8. **resident 同居は `ui --resident`。** 既定 `ui` は編集専用。
9. **保存後の runtime 追従は Host が `RequestProfileChange`。** UI が「再起動せよ」と嘘をつかなくて済む。やらなければ段階は「未反映」のまま（許容、ただし使い勝手は落ちる）。

オーナー／統括がひっくり返すなら、効くのは 8 と 9 だけです。画面形（案A）と editor の単一性は Exit 文面からほぼ決まっています。