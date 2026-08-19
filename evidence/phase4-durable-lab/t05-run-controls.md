# t05-run-controls — 証跡

- 実装: hinata（2026-08-19）
- base: 36185d4（t04・t06 着地後の main。t09/t08/t10 は本 base に含まれない——着地順による）
- 対象: PB-007（pause・一手実行・skip・abandon・手動介入・未来手順の編集・version switch）、
  PB-013（物理入力の manual intervention 仲裁・帰属禁止）、§6.5 仲裁・§6.7 契約・§6.8

## 何を作ったか

### 1. journal 閉集合の拡張（Contracts／RunJournal／run-event.md）

- `RunEventPayloadTypes` へ `skip`・`abandon`・`version-switch` の3定数を追加（8種→11種。t09 が
  RunProjection に残した「正規の version switch は t05 所有」の縫い目の供給側。文字列は room [90] の
  interface 決定どおり）。
- `RunJournal` の append 検証に追加: skip は `NodeOrTransitionId` 必須（§6.8「どの手順を飛ばしたか」が本体）、
  制御3種は `ActorType=User` 以外を拒否（PB-013: 制御操作を自動化へ帰属させない）。
  manual-intervention の User 制約は `RunControls` 側が持つ——t03 確定の journal 検証を遡って変えない。
- `docs/contracts/run-event.md` へ閉集合・必須 ID 表・run 制御 event の意味を正本化。
  **pause／resume は journal 対象外**: durable な進行効果が無く（再起動後に自動で走り出す経路が無い）、
  記録すべき「進行の変更」が無い。

### 2. `RunControlState`（Domain・pure 状態機・新規）

Running／Paused／ManualIntervention／Abandoned の4相＋2フラグ（NeedsReobservation・ObservedInCurrentHold）。

- 一手実行・version 切替の可否を相と再照合状態から決定的に導出（`CanStep`／`CanSwitchVersion`）。
- 介入終了→Paused＋NeedsReobservation。新しい Observation の記録まで resume／step／switch 全て拒否（§6.8）。
- **介入中の ObservationRecorded は例外**——journal 上「介入開始と終了の間に observation event が現れない」
  は t10 再開照合（すずね所見 room [101]②）が依存する契約であり、状態機が構造で保証する。
- 停止位置ごとの再照合: Pause で ObservedInCurrentHold をリセットし、前回停止の観測を持ち越さない。
- Abandoned は終端。全操作が例外。

### 3. `RunControls`(Playbooks・統括・新規)

- **pause／resume**: 状態遷移のみ（journal なし・上記根拠）。
- **一手実行 `StepOnce`**: Paused かつ再照合待ちでない時だけ、既存 `AttemptDispatchGate.ArmThenDispatch` を
  1回通す。実行後も Paused——自動継続の経路が無い。
- **`Skip`**: skip event を journal へ。dispatch も Attempt も作らない。対象 node/edge が pin 済み version に
  実在しないものは拒否。
- **手動介入**: 開始・終了とも manual-intervention event（区別は payload・同 type 2 event、room [97] で
  t10 と合意した表現）。開始で executor 停止相当（CanDispatch=false）。
- **`OnPhysicalSemanticAction`**（PB-013・§6.5 の仲裁。方式は計画の宣言どおり「停止」を採用——マスクしない）:
  Run の Playbook が使う Semantic Action への物理入力→manual intervention として記録し停止
  （`ExecutorStopped`）。介入中の追加入力は event を増やさない（`AlreadyIntervening`）。
  Playbook が使わない action は Run の関知外（`NotBoundToRun`——通常の mapping 配送はそのまま）。
  Run 進行へ合流する戻り値・経路は存在しない。
- **`Abandon`**: run 単位の abandon event を journal へ記録し、進行中 Attempt を §6.7 の合法経路だけで終端へ:
  dispatch 前→Cancelled（PB-007 の写像）、dispatch し得た後→OutcomeUnknown→Reconciling→Abandoned。
  Attempt ごとの終端 event は持たない（run 単位の abandon event が正）。
- **`SwitchVersion`**: Paused＋現在停止位置での再観察後だけ（§6.8）。進捗継承は同一 stable node ID かつ
  Preconditions／ExpectedOutcomes 一致の node だけ、継承できない切替は拒否。event は新 version を運ぶ
  （pin と異なる version を運んでよい唯一の event）。切替後も Paused のまま。
- **未来手順の編集**: 新実装なし——PB-008 の `PlaybookCorrection.Revise`（t02）が既に「新 version を作り
  実行済み event を変えない」編集を提供しており、Run への反映は本 task の `SwitchVersion` が閉じる。
  二重化しない。
- 全操作は現在 Run の RunId・PlaybookId・pin を運ぶ event だけを受ける（version-switch のみ新 version）。

### 4. `AttemptDispatchGate.Recover` の abandon 対応

abandon event のある Run の Attempt を `RunControls.Abandon` と同じ分類で復元:
confirmation 済み→Confirmed／dispatch し得た未確定→Abandoned／dispatch 前→Cancelled。
abandon 済み Run の Attempt が復元で OutcomeUnknown として蘇り、契約5で次 dispatch を永久に塞ぐ経路を消した。

## どう確認したか（最終試験結果）

worktree（base 36185d4 の clean checkout）内で focused test を実行。build 出力は一時
`Directory.Build.props`（宣言境界外・commit 前に削除済み）で scratchpad へ redirect（手法は room [15]）。
architecture test は test dll の位置から `OpenLogicool.sln` を上方探索するため、redirect 先に
sln コピー＋`src`/`tests` junction を置いて実 worktree の csproj を読ませた（テスト対象は実物・測定器のみ調整）。

- `dotnet test tests/OpenLogicool.Domain.Tests` → **52件 green**（既存43＋RunControlStateTests 9）
- `dotnet test tests/OpenLogicool.Playbooks.Tests` → **68件 green**（既存44＋RunControlsTests 18＋
  RunJournalTests 追加4＋AttemptDispatchGateTests 追加1。既存の閉集合全 type 受理 test は
  11種＋type 別 actor/nodeId 供給へ更新——skip の nodeId 必須・制御3種の User 必須を journal 段で検証）
- `dotnet test tests/OpenLogicool.Architecture.Tests` → **4件 green**（Contracts 変更後も参照方向・package 方針は不変）

検証の要点:

- PB-007: step は1 dispatch だけで Paused 維持・Running では step 不可・skip は Attempt を作らない・
  abandon 後は全制御が拒否される。
- PB-013／§6.5: bound action の物理入力→ExecutorStopped＋manual-intervention event、unbound→無記録、
  介入中→event 追加なし。Run 進行へ合流する経路の不在は API 形状（戻り値のみ）で構造保証。
- §6.8: 介入終了→再観察まで resume/step/switch 全拒否。介入中の observation 記録は拒否（t10 前提の journal 並び）。
  switch は「Paused だけ」「Paused＋観測なし」「介入後観測なし」「node 欠落」「condition 不一致」
  「event が旧 version」「同一 version」の7拒否と成立系（re-pin＋以後の event は新 version 強制）を確認。
- §6.7×OPS-008: abandon 済み run の Recover 分類（Cancelled／Abandoned／Confirmed 維持）と、
  復元後に次 dispatch が契約5で塞がれないことを確認。

## 監査へ（見てほしい点）

1. run-event.md へ足した run 制御 event の意味が §6.8・PB-007/013 の写しとして過不足ないか
   （特に「pause/resume を journal 対象外」とした根拠と、「介入開始〜終了間に observation なし」の契約化）。
2. `Abandon` の Attempt 終端化が §6.7 の遷移図に無い近道を作っていないか（実装は ResolveLocally の
   連鎖で Domain 検証を都度通す——迂回口の有無）。
3. `Recover` の abandon 分類が「journal に記録の無い解決を信じない」原則と両立しているか
   （run 単位 abandon event を Attempt 終端の根拠に使う読みの妥当性）。

## 統合面（本 task の受入条件外・bell 仕分け済み room [94][109]）

- canonical の `RunProjection.Tally`／`SessionRecorder/Replayer`（t09・本 base に無い）への3 type 統合と
  version-switch の pin 追従。
- t10 の wire literal（abandon/version-switch）→ `RunEventPayloadTypes` 定数参照への置換。
