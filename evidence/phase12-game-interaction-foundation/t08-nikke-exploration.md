# t08 NIKKE複数候補 zero-seed探索

判定日: 2026-08-25

## 結論

同一process、同一`ProductGameExplorerRuntime`、同一SQLite接続で3候補を順に探索し、2件の確定遷移と1件のOutcomeUnknown停止を得た。game固有state名、target名、座標、正解routeはseedしていない。各targetはNanoで一回だけdispatchし、3件目の未確定結果は自動再試行せず停止した。本runのharness判定は`Passed=false`であり、Exitの成功証拠へ読み替えない。逐次索引と既知実行の正証拠は[t09](t09-incremental-known-screen-index.md)とする。

診断証拠は `probe-output/game-interaction-foundation-explore-run-20260825-122823-092.json`（`Passed=false`）。

## 連続run結果

| step | target | freshness | 判定 | 学習 |
|---|---|---:|---|---|
| 1 | `ROOM` | 34 ms | Stable 2 / Moved | Novel |
| 2 | `シミュレーション開始` | 35 ms | Stable 2 / Moved | Novel |
| 3 | `当シミュレーション開始` | 41 ms | TimedOut / Undetermined | OutcomeUnknown |

- RequestedSteps: 3
- CompletedSteps: 3
- DistinctTargetKeys: 3
- SendInput: 0
- Computer Use: 0
- fallback: 0
- automatic retry: 0
- Nano AllUp: true

step 1と2は異なる画面nodeと遷移edgeを同じDBへ保存した。step 3は有効観測1件で確定せず、OutcomeUnknownを保存してrunを止めた。この停止は設計メモの「未確定結果を自動再試行しない」に一致する。

NoChange保存は `probe-output/game-interaction-foundation-explore-step-20260825-114101-388.json`（Windows title bar）と `probe-output/game-interaction-foundation-explore-step-20260825-114333-615.json`（右上HUD）で成立した。両者はNoChange学習後、Windows専用risk policyで後続dispatch対象外にした。

## 非戦闘scopeと復帰

3件目は戦闘開始前のシミュレーションルームbuff選択画面へ到達した。戦闘は開始せず、探索を終了した。`probe-output/game-interaction-foundation-observe-20260825-122937-220.json`が開始前画面を保存する。Esc 1回でアークへ復帰し、`probe-output/game-interaction-foundation-observe-20260825-123106-651.json`とPNGでアーク画面を目視確認した。

この実測を受け、SafeMenu既定へ`開始`／`start`を`activity-start`禁止語として追加した。以後、同じ非戦闘scopeでは開始操作をdispatchしない。

## t08中に根治した基盤欠陥

1. `MaximumRepeatedProbeCount=1`が最初のprobe直後にRun全体を停止するoff-by-oneを`count > maximum`へ修正した。
2. 4B推論中にWGC event queueへ溜まった古いframeを返してfreshness 5296 msになったため、WGC内部でGPU copy前に古いframeをdisposeし最新frameだけをCaptureするよう修正した。実機Freshness 34 msで確認した。
3. Host側drainはアニメーション中に無限化しないよう最大2frameへ制限した。
4. 観測処理中もwait timeoutの残り時間Cancellationを適用し、60秒契約を越えるhangを解消した。
5. OCR最大spanを文字高さ・領域優先で24件へ、AI返却を12件へ制限し、JSON truncationを解消した。dropはnormalization証拠へ残す。
6. `qwen3-vl-2b`と4Bの同時常駐でGPU空き301 MBになりtimeoutしたため、公式Foundry CLIで2Bだけunloadした。4B単独後は空き17.9 GB。

## 検証

- ExplorationCoordinator repeated limit focused: 2 green
- GameTransitionJudge focused: 11 green
- GameInteractionStabilityRuntime focused: 4 green
- Windows WGC Host adapter focused: 3 green
- Capture WGC focused: 3 green
- Foundry label client/provider focused: 24 green
- Windows OCR canonicalization focused: 2 green
- Windows risk policy focused: 2 green
