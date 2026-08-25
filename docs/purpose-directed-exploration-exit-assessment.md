# 目的指向の逐次探索 Exit判定

判定日: 2026-08-26

## 結論

Exit成立。利用者goalを受け、現在pageの保存actionを先に使い、無い時または10秒非遷移時だけAIで一件を探索し、`Moved`だけをnode／edge／Learning Routeへ逐次保存する製品runtimeが成立した。正常stepと旧route revisionは保持し、修復は失敗stepだけを新版edgeへ差し替える。

## 受入判定

| 条件 | 判定 | 根拠 |
|---|---|---|
| 上位機能は10基盤だけを通る | 確認済み | `PurposeDirectedExplorationRuntime`は`IProductGameStepRuntime`として既存`ProductGameExplorerRuntime`だけを呼ぶ。 |
| 保存action無し／10秒非遷移だけAI | 確認済み | known-first、comparison-only、force repairのfocused test。実機learnは空routeでAI 1、replayはAI 0。 |
| `Moved`を逐次route保存 | 確認済み | 実機learn `005402-987`でroute 0→1 Compiled。focused testで複数edge append。 |
| `Stayed`／`Undetermined`をRun失敗へ丸めない | 確認済み | 非遷移は`LearningContinues`。実機の誤grounding／非遷移を不採用結果として保持。 |
| 失敗stepだけ修復し正常stepを作り直さない | 確認済み | SQLite focused testで対象indexだけ差替え、旧revision保持。正常replayは既存edgeを再commitしない。 |
| 実目的を初回から完了 | 確認済み | NIKKE「アークを開く」: AI 1、Nano click、10,059ms・19観測、Moved、button／座標／destination／edge／route保存。 |
| 再起動後AI 0再現 | 確認済み | ダイアログ無しロビーから別process `010722-497`: saved-route、AI 0、10,087ms・18観測、Moved、同一edge、route 1→1。after実画像はアーク画面。 |
| 禁止入力経路0 | 確認済み | learn／replayともSendInput 0、Computer Use 0、fallback 0、自動retry 0、Nano Serial HIDのみ。 |
| 終端品質 | 確認済み | 円卓反証 #977 PASS、受入監査 #979、関連8 project・516件green、full regression 22 project・1192件green。 |

## 不採用結果

- 60秒観測の旧Runは10秒裁定を満たさないため不採用。
- 目的と異なるAI target、非遷移、OCR完全一致拒否、anchor 2件拒否、空文字completionを成功へ丸めず、原因修正後に取り直した。
- replay `005826-295`は終了確認ダイアログを閉じたfalse positiveで、円卓の実画像反証により撤回した。最終採用は`010722-497`だけである。

## claim境界

確認済みはWindows 11、NIKKE実window、現在のロビー→アーク1目的、Foundry Local Qwen3-VL-4B、Nano Serial HIDの組合せである。一般gameの無人自律完走、複数primitiveを混在させた実目的、戦闘・課金・消費操作、失敗stepを意図的に壊すlive修復はclaimしない。失敗step差替えは実SQLite focused testの確認済み範囲とする。

詳細証拠は[evidence/purpose-directed-exploration/t04-live-attempts.md](../evidence/purpose-directed-exploration/t04-live-attempts.md)と[t05-terminal.md](../evidence/purpose-directed-exploration/t05-terminal.md)。
