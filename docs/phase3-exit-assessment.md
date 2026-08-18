# Phase 3 Exit Assessment（App-first Unified UX）

- 作成: 2026-08-16（統括 bell-fable・t11）
- 上位正本: [development-plan.md](development-plan.md) §Phase 3（Exit 条件の文言はそちらが正）
- 判定材料: Lattice plan `phase3-app-first`（t01〜t10 done）・evidence/phase3-app-first/・probe-output/
- 根拠4値: 確認済み（実測あり）／強い推定（構造・対称性による）／未確認／非対応

## Exit 5条件の判定

### 条件1: 利用者が app を一度選び、device 別画面を往復せず両機を設定 — **確認済み**

- Workspace Command Surface 1画面（オーナー承認モック [ui-mocks/](ui-mocks/) 準拠・t09）: 上部でアプリ選択 → 左の操作一覧 → 中央 G13/G600 タブ＋実配置模式図 → 右の割当パネル。device 別画面は存在しない（別 Window は診断のみ）。
- 実測: t10 scenario（アプリ選択→操作作成→両 device binding→保存→適用状態表示）が実 DB・実列挙で成立（`probe-output/ui-test-scenario-20260816-124626-874.json`）。WPF クリック列ではなく、Window が呼ぶのと同じ public 経路（`IWorkspaceEditorIntents`）の headless 実測＋一画面構成の統括目視・オーナー方向承認。
- 注記（監査指摘・Exit 判定不変）: UI の保存は app 関連付けを作らない（関連付けは CLI `associate` のみ）。設計文書との差分として残課題に記録。

### 条件2: OpenLogicool へ Alt+Tab しても editing target を失わない — **確認済み**

- 構造: 編集対象 workspace は上部 pill の明示選択だけで変わる。foreground 追跡（200ms poll）は runtime profile 切替専用で、編集画面の状態へ到達する経路がない（MAP-010 系の分離）。
- 実測（2026-08-17・[t11-live-checks.md](../evidence/phase3-app-first/t11-live-checks.md)）: 未保存変更を作って Alt+Tab 往復→編集内容・未保存表示とも保持をオーナー目視で確認。

### 条件3: launcher・同名 EXE・window 消失を誤 profile で継続しない — **確認済み**

- p1-core live run（[p1-core-live-run.md](../evidence/phase3-app-first/p1-core-live-run.md)）: EXE full path 完全一致切替（explorer）・package identity 一致切替（Store メモ帳・APP-004）・往復の理由付き log（APP-005）・昇格 process で誤 Unknown なし（APP-008）。window 消失（ForegroundState 3状態・取得不能は既定へ）は focused test で検証済み。
- t11 実機確認（2026-08-17・[t11-live-checks.md](../evidence/phase3-app-first/t11-live-checks.md)）で残項目が成立: **NIKKE launcher→本体遷移**（launcher 前面で本体用へ誤切替なし・本体で path 一致・終了後に既定へ復帰）＋**Unknown 実トリガー初実測**（anti-cheat 保護で identity 取得不能→「Unknown Application」明示＋既定適用——黙って誤 profile 継続しない）。

### 条件4: G13 だけ／G600 だけでも完結する — **確認済み（G600 単体）／強い推定（G13 単体）**

- 構造: `ResidentInputHost` は profile の無い device 種別を配線しない（`DefaultByKind` に無い種別は skip）、`AppProfileResolver` は黙って既定値を作らない（複数 profile で既定欠落は例外）。workspace compile は device 種別ごとに独立の profile を生成。未割当 action は warning で編集・保存を妨げず、片側 0 台でも編集可（`WorkspaceScreenProjection`）。
- 実測: G600 単体は hotplug／sleep smoke で成立（probe は G13 を必要としない。fastpath smoke は両機同時のため単体証跡には数えない）。G13 単体は対称構造だが単体実測は未記録。

### 条件5: UI test scenario が fake と real contract で同じ結果になる — **確認済み**

- t10（[evidence/phase3-app-first/t10-ui-test-contract.md](../evidence/phase3-app-first/t10-ui-test-contract.md)）: 同一 scenario を fake（in-memory store・固定列挙）と real（実 SQLite・実列挙・実機 G13/G600 接続）で実行し、field 単位の機械突合で **IsMatch=true・不一致0件**。除外は実機接続台数表示（環境依存・理由明記）と、本番 rail 構築（`ApplicationWorkspaceCatalog`＋`RunningApplicationCatalog`——scenario は固定 rail を両側で使うため比較対象外。t10 証跡どおり）。fake/real が同一ロジックを通ることは共有関数抽出（`WorkspaceEditorIntentsSupport`）で構造保証。fake 側 scenario は focused test 9件として常設。

## full regression

2026-08-16（t11・commit 6ab7dc4 時点）: `dotnet test OpenLogicool.sln` — 14 test project・計 314 件、全 green・失敗0。

## cross-provider 監査

2026-08-16（Grok 4.6・aiterm read-only 強制・監査対象は本書草案＋t10 実装＋証跡）:

- **重大（判定を変えるべき）: なし**。判定要旨「確認済み3／強い推定2」に同意、過大評価なし、条件2・3の強い推定根拠はコード上事実と確認。
- 軽微（文言修正）5件を本書へ反映済み: ①条件4の型名（`HostProfileSelection`→実体は `ResidentInputHost`／`AppProfileResolver`）②条件4の G600 単体証跡は hotplug/sleep に限定（fastpath は両機同時）③条件1の実測は「Window と同一 public 経路の headless 実測」と明記④条件5に rail 比較対象外を明記⑤UI 保存が関連付けを作らない設計差を注記化。
- 補足: 当初の監査役 Codex はこの Windows 環境で実行不能（codex-sidecar・CLI sandbox とも既知の罠。罠DB記録済み）のため、正典の代替である Grok へ交代した。

## 残課題（Exit 判定外・次フェーズ以降）

- UI 保存と app 関連付けの導線統合（現状 UI は保存のみ・関連付けは CLI `associate`。設計文書との差分——監査指摘⑤）
- G13 単体環境の実測（対称構造のため強い推定のまま運用可）

## オーナー手番の実機確認（2026-08-17 完了）

4点＋持ち越し全て成立（[t11-live-checks.md](../evidence/phase3-app-first/t11-live-checks.md)）: ①Alt+Tab 編集対象保持 ②NIKKE launcher→本体遷移＋Unknown 実トリガー初実測 ③`ui --resident` 保存→即時反映 ④キー録画 modal 目視。確認中に発見したバグ2件（キー録画の IME 化け・resident 同居時の raw input 登録横取りで実機入力全死）はいずれも原因特定→根治→focused test green→push 済み（13d35cd・3584440）。

## 判定要旨

Exit 5条件は **確認済み5**（条件4の G13 単体のみ対称構造による強い推定を残すが、条件としては G600 単体実測で成立）。未確認・非対応の条件はない。最終 Exit 宣言はオーナー裁定。
