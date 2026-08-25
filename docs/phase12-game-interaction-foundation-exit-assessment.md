# Phase 12 Game Interaction Foundation Exit判定

判定日: 2026-08-25

## 結論

最終判定中。低レベル基本操作、逐次Screen Index、製品Host初回discover、再起動復元、Foundry停止中のAIなし既知実行はNIKKE実画面で成立した。full regressionはgreenだが、zero-seed複数targetとicon/image discoveryが未成立のためPhase 12全体Exitは宣言しない。

公開可能な主張は「Game Operator Previewの画面操作foundation」と「候補Game Structureの学習」までである。一般game対応、無人日課、Verified Autonomous Playbook、戦闘、課金・消費操作は含まない。

## Exit判定

| 条件 | 4値 | 根拠 |
|---|---|---|
| 基本10機能が明示contractと一経路の製品runtimeを持つ | 確認済み | `GameInteractionContracts.cs`、t01〜t06 evidence |
| 10機能をNIKKE実画面で個別実証する | 強い推定 | [t07](../evidence/phase12-game-interaction-foundation/t07-basic-live.md)。Scroll／Dragのゲーム内受理は未確認 |
| raw pixel差分でなく意味構造でMoved／Stayed／Undeterminedを分ける | 確認済み | animation中Stayed、ランキング往復Moved、timeoutはUndetermined |
| 同一runtime／同一DBでzero-seed複数targetを一回ずつ探索する | 未確認 | [t08](../evidence/phase12-game-interaction-foundation/t08-nikke-exploration.md)は`Passed=false`。未確定の自動再試行0だけ確認済み |
| actual inputはNanoだけで、SendInput／Computer Use／fallback 0 | 確認済み | t07／t08全ライブJSON |
| OS依存capture／OCR／座標／risk処理が独立fileにある | 確認済み | `WindowsWgcGameFrameSource.cs`、`WindowsGameFrameRecognitionAdapters.cs`、`WindowsGameInteractionCoordinateMapper.cs`、`WindowsGameExplorationCandidateRiskPolicy.cs` |
| WGC backlog、provider timeout、GPU resourceを実測して閉じる | 確認済み | latest-frame drain、in-flight timeout cancellation、4B単独時GPU空き17.9GB |
| 非課金・非消費・非戦闘scopeを越えない | 確認済み | 戦闘開始前で停止・アーク復帰、`開始`／`start`をactivity-start禁止へ追加 |
| 初回AI発見を逐次保存し、同一操作の2回目をAIなしで実行する | 確認済み | [t09](../evidence/phase12-game-interaction-foundation/t09-incremental-known-screen-index.md) |
| 文字・icon・画像領域をDiscoverTargetsで扱う | 未確認 | 文字labelだけ確認済み。icon-only／画像領域は非対応 |

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
2. icon-only controlは非対応。生VLM座標へfallbackしない。
3. t08の3件目はOutcomeUnknownであり、成功へ丸めていない。未確定結果の自動再試行は0。
4. 候補edgeはCandidate／Novelであり、このExitだけでVerified Autonomous Playbookへ昇格しない。
5. Foundry Local modelの取得・常駐は開発環境条件であり、配布物へのmodel同梱を決めていない。
6. Phase 9の過去ExitはGame Structure Explorer Previewの契約／GameLab／限定live証拠を閉じたものとして維持するが、基本操作foundationと複数候補の自走成立は本campaignで初めて確認した。

## 検証

- AI: 38 green
- Exploration: 35 green
- Host: 171 green
- Capture: 16 green
- Conformance: 61 green
- Probe: 61 green
- solution full regression: 1156 green、失敗0（2026-08-25、t09最終gateで一回実行）

## 工程

- 計画正本: [campaign plan](phase12-game-interaction-foundation-campaign-plan.md)
- 工程正本: Lattice plan `phase12-game-interaction-foundation`
- 基本実機: [t07](../evidence/phase12-game-interaction-foundation/t07-basic-live.md)
- 複数候補実機: [t08](../evidence/phase12-game-interaction-foundation/t08-nikke-exploration.md)
