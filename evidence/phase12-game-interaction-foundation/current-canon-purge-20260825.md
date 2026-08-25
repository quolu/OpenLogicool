# Game Interaction現行裁定統合・旧gate撤去

判定日: 2026-08-25

## 結論

2026-08-25のオーナー裁定をGame Operatorの最上位正本へ統合し、反する現行コード経路、既定値、UI、テスト期待、計画記述を撤去した。過去evidenceとlegacy journal payload名はreplay用に保持するが、新規操作の権限、拒否、合否には使わない。

## 現行runtime

- `WindowsKnownFirstTargetDiscovery`を追加し、保存page／actionをAIより先に解決する。
- AI discoveryは保存action無しと、保存actionの10秒非遷移後だけ起動する。
- `ProductGameExplorerRuntime`の送出前追加AI観測、候補履歴による再操作拒否を削除した。
- OCR文字から「購入」「戦闘」「開始」等を禁止tagへ変換する既定policyを削除した。
- Windows adapterはwindow非クライアント領域だけを拒否し、HUD、終了modal、button文字を拒否しない。
- 一手承認、既知復帰edge、反復回数、no-progress、oscillationをExploration admission／停止条件から削除した。
- stale／capture unavailableはRun全体を壊さず、そのObservationだけを拒否して再観測できる。
- `Stayed`／`Undetermined`は学習結果として保存し、Run全体の失敗にしない。
- Structure revision IDが更新されても参照edgeが現在Structureで有効ならrouteをSupervised実行できる。
- verificationとGame Policy review statusを操作拒否から外し、明示`AllowedModes`と明示禁止tagだけをgateにした。
- 旧`AuditAfter` destination完全一致、Teach approval型、`capture-dispatch`／UniqueMatch resume、StructurePlaybook approval経路を削除した。
- 新規journalは`authorization`を使い、`approval`は既存journal replayだけに残した。

## 正典

- `AGENTS.md`へ成熟基盤優先と操作拒否の所有境界を追記した。
- `docs/development-plan.md`を1.0へ更新し、§0.3を現行最上位裁定とした。
- Foundation、Learning Console、Grounding、Game Policy、Phase 9〜12文書を現行裁定へ統一した。
- Phase 4／6／7／9／10の旧gateはhistorical acceptanceと明記した。
- t08の3候補中1件`Undetermined`をRun全体失敗とした旧`Passed=false`判定を失効させ、3候補の各一回dispatchと全outcome保存をFoundation成立証拠として採用した。

## 検証

- 変更直結8 project: 700件green。
- solution full regression: 22 test project、1166件green、失敗0。
- solution build: warning 0、error 0。
- `git diff --check`: error 0。
- 残存語監査: `NeedsApproval`、`ApprovalReason`、`SafeMenuDefault`、`RecoveryLost`、`UniqueMatch` resume、旧StructurePlaybook approvalの製品／test参照0。

## 非変更

- current window／frame／transform、Nano capability、durable commit、明示Game Policyは実境界として維持した。
- 既存のProbe系未コミット差分3ファイルと未追跡の実験出力には触れていない。
