# Phase 12 Game Interaction Foundation Exit判定

判定日: 2026-08-25

## 結論

**10の基盤機能のExitは成立した。** Observe、DiscoverTargets、Hover、Click、KeyTap、Scroll、Drag、WaitStable、Compare、LearnTransitionは、公開contract、製品runtime、Host／UI port、GameLab、NIKKE実測、逐次Screen Index、AIなし既知実行まで成立した。

Phase 12全体の上位条件「同一runtime／同一DBでのzero-seed複数target探索」は`t08`の失敗証拠から更新していないため、Phase 12全体Exitは宣言しない。これは10の基盤機能の未完成ではなく、それらを使う上位探索loopの未確認である。

公開可能な主張は「Game Operator Previewの画面操作foundation」と「候補Game Structureの学習」までである。一般game対応、無人日課、Verified Autonomous Playbook、戦闘、課金・消費操作は含まない。

## Exit判定

| 条件 | 4値 | 根拠 |
|---|---|---|
| 10の基盤機能が明示contractと一経路の製品runtimeを持つ | 確認済み | `GameInteractionContracts.cs`、t01〜t07 evidence |
| 10の基盤機能をNIKKE／GameLabで個別実証する | 確認済み | [t07](../evidence/phase12-game-interaction-foundation/t07-basic-live.md)。NIKKEでClick／KeyTap／Scroll／Drag、GameLabでHover表示反応。NIKKEの非反応Hoverは`Stayed`を維持 |
| raw pixel差分でなく意味構造でMoved／Stayed／Undeterminedを分ける | 確認済み | animation中Stayed、ランキング往復Moved、timeoutはUndetermined |
| 同一runtime／同一DBでzero-seed複数targetを一回ずつ探索する | 未確認 | [t08](../evidence/phase12-game-interaction-foundation/t08-nikke-exploration.md)は`Passed=false`。未確定の自動再試行0だけ確認済み |
| actual inputはNanoだけで、SendInput／Computer Use／fallback 0 | 確認済み | t07／t08全ライブJSON |
| OS依存capture／OCR／座標／risk処理が独立fileにある | 確認済み | `WindowsWgcGameFrameSource.cs`、`WindowsGameFrameRecognitionAdapters.cs`、`WindowsGameInteractionCoordinateMapper.cs`、`WindowsGameExplorationCandidateRiskPolicy.cs` |
| WGC backlog、provider timeout、GPU resourceを実測して閉じる | 確認済み | latest-frame drain、in-flight timeout cancellation、4B単独時GPU空き17.9GB |
| 非課金・非消費・非戦闘scopeを越えない | 確認済み | 戦闘開始前で停止・アーク復帰、`開始`／`start`をactivity-start禁止へ追加 |
| 初回AI発見を逐次保存し、同一操作の2回目をAIなしで実行する | 確認済み | [t09](../evidence/phase12-game-interaction-foundation/t09-incremental-known-screen-index.md) |
| 文字・icon・画像領域をDiscoverTargetsで扱う | 確認済み | `host-visual-image-discover-ai1.json`は`kind=icon`の局所visual targetを保存し、`host-visual-image-known-execute-ai0.json`でAI0再実行成立 |

## 実測構成

- Windows 11 native
- PowerShell 7／Aiterm開発shell
- NIKKE 2720×1197 window capture
- Windows Graphics Capture
- Windows.Media.Ocr 2倍scale
- Foundry Local `qwen3-vl-4b-instruct-cuda-gpu:2`
- Nano Serial HID COM8
- Computer Use停止、dispatch 0

## 判定上の限界

1. 4Bもclickabilityを完全には分類せず、同一frame OCR候補24件・返却12件上限・候補外dropの決定的制約が必要である。
2. icon／画像controlは同一frameの局所visual patchへ束縛する。生VLM座標へfallbackしない。
3. t08の3件目はOutcomeUnknownであり、成功へ丸めていない。未確定結果の自動再試行は0。
4. 候補edgeはCandidate／Novelであり、10の基盤機能ExitだけでVerified Autonomous Playbookへ昇格しない。
5. Foundry Local modelの取得・常駐は開発環境条件であり、配布物へのmodel同梱を決めていない。
6. Phase 9の過去ExitはGame Structure Explorer Previewの契約／GameLab／限定live証拠を閉じたものとして維持する。zero-seed複数targetの自走は今回も未確認である。

## 検証

- 変更直結関連test: AI 41、Exploration 44、Perception 30、Host 198、Persistence 50、GameLab Discovery 11、GameLab Prototype 3、Architecture 8。合計385 green。
- solution full regression: 1202 green、失敗0（2026-08-25、独立反証修正後の最終gate）。
- diff監査: `git diff --check`通過。独立反証のP1 4件／P2 1件（destination意味混同、古い安定候補、fault前cache、exact比較へのfuzzy混入、終端OCR欠測）を修正し、負例を追加。最終再監査で重大指摘なし。

## 工程

- 計画正本: [campaign plan](phase12-game-interaction-foundation-campaign-plan.md)
- 工程正本: Lattice plan `phase12-game-interaction-foundation`
- 基本実機: [t07](../evidence/phase12-game-interaction-foundation/t07-basic-live.md)
- 複数候補実機: [t08](../evidence/phase12-game-interaction-foundation/t08-nikke-exploration.md)
