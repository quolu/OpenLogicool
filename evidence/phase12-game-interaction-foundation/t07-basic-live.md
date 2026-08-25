# t07 基本10機能 NIKKE 実機成立

判定日: 2026-08-25

## 結論

NIKKE実画面、WGC、Windows OCR、Foundry Local `qwen3-vl-4b-instruct-cuda-gpu:2`、Nano Serial HIDを接続し、基本10機能の製品runtime経路を実測した。実入力はNanoだけを通り、SendInput、Computer Use、fallback、自動retryは全証拠で0である。Scroll／DragはNano送出と安全なreleaseまでの確認で、ゲーム内効果の受理は未確認である。

## 基本機能と証拠

| 機能 | 実測結果 | 証拠 |
|---|---|---|
| Observe | 元PNG、SHA-256、2720×1197を保存 | `probe-output/game-interaction-foundation-observe-20260825-103639-945.json` |
| DiscoverTargets | 同一frame OCR候補へAI出力を制約し、アーク画面の対象をframe-bound化 | `probe-output/game-interaction-foundation-discover-targets-20260825-113448-902.json` |
| Hover | `アーク`へNano pointer dispatch 1、実座標receipt取得 | `probe-output/game-interaction-foundation-hover-20260825-110041-503.json` |
| Click | ランキング操作へNano click dispatch 1 | `probe-output/game-interaction-foundation-explore-step-20260825-114915-963.json` |
| KeyTap | ランキング→アークをEsc 1回で復帰 | `probe-output/game-interaction-foundation-key-tap-20260825-114814-878.json` |
| Scroll | wheel 1、アーク→アーク、Stayed。Nano送出確認／ゲーム内受理は未確認 | `probe-output/game-interaction-foundation-scroll-20260825-111900-608.json` |
| Drag | down→move→up、アーク→アーク、Stayed。Nano送出確認／ゲーム内受理は未確認 | `probe-output/game-interaction-foundation-drag-20260825-111938-886.json` |
| WaitStable | 候補数35→12→34の部分検出を意味集合で統合しStable 3 | `probe-output/game-interaction-foundation-wait-stable-20260825-114010-435.json` |
| Compare | ランキング→アークをMovedと判定 | `probe-output/game-interaction-foundation-key-tap-20260825-114814-878.json` |
| LearnTransition | アーク→ランキングをNovelとして永続化し、Structure revision作成 | `probe-output/game-interaction-foundation-explore-step-20260825-114915-963.json` |

最終探索一手の結果は `Passed=true`、`Status=Learned`、`Stability=Stable`、`StableFrames=3`、`Judgement=Moved`、`Outcome=Novel`、`DispatchCount=1`、`AllUp=true` である。

## 実測から直した欠陥

1. HostのExploration参照追加後にNuGet lockが未更新だったため、対象lockを公式restoreで更新した。
2. 操作直後の旧frameをStable扱いしてEsc結果をScrollへ誤帰属したため、minimum stability window後から観測を開始した。
3. KeyTapが不要なtarget選択を要求していたProbe配線を分離した。
4. `?`／`？`／句読点欠落を別状態にしていたため、意味ラベルをUnicode・空白・句読点で正規化した。
5. OCR候補外のAI幻覚をclick対象にしないよう、同一frame OCR候補制約とnormalization証拠を追加した。
6. 同一画面で候補集合が揺れるため、3件以上・少ない側80%以上包含・非header共通2件の実測条件で意味安定を判定した。
7. provider Unknownの空認識を別画面扱いせず欠測として保持した。
8. WGCに含まれるWindows title barと画面端HUDをWindows専用risk policyでdispatch前に除外した。
9. `Learned/OutcomeUnknown`をProbe PASSにしていた条件を、Stableかつ判定確定かつOutcomeUnknown以外へ修正した。

## モデル裁定

2BはOCR候補列をほぼ全件返し、clickability判定が弱かった。RTX 5090 32GB／空き8.7GBを実測後、公式Foundry CLIで4Bをロードした。4Bも候補外を混ぜるため、候補内だけを採用してdropを証拠化する。8Bは未使用である。

## 残り

t08では同一runtime・同一DBで複数stepを継続し、同じ意味対象の再試行禁止、復帰edge、複数node／edgeの永続化を実証する。
