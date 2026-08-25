# t07 10の基盤機能 NIKKE 実機成立

判定日: 2026-08-25

## 結論

NIKKE実画面、WGC、Windows OCR、Foundry Local `qwen3-vl-4b-instruct-cuda-gpu:2`、Nano Serial HIDを接続し、10の基盤機能の製品runtime経路を実測した。実入力はNanoだけを通り、SendInput、Computer Use、fallback、自動retryは全証拠で0である。後続実測でScroll／Dragのゲーム内内容移動、KeyTapの索引再実行、10秒の遷移監視まで確認した。HoverはNIKKEの2対象が`Stayed`であり、対応UIを持つGameLabで表示反応を確認した。

## 10の基盤機能と証拠

| 機能 | 実測結果 | 証拠 |
|---|---|---|
| Observe | 元PNG、SHA-256、2720×1197を保存 | `probe-output/game-interaction-foundation-observe-20260825-103639-945.json` |
| DiscoverTargets | 同一frame OCR候補に加え、局所visual patchへ束縛したicon／画像controlを初回発見し、AIなし再実行 | `host-visual-image-discover-ai1.json`、`host-visual-image-known-execute-ai0.json` |
| Hover | NIKKEの保存済みbuttonは`Stayed`。GameLabのOpenEventは白→青、索引保存、AIなし再実行で`Stable`＋`Moved` | `host-hover-friend-known-ai0.json`、`gamelab-hover-before.png`、`gamelab-hover-after.png`、`gamelab-hover-known-ai0.summary.json` |
| Click | ランキング操作へNano click dispatch 1 | `probe-output/game-interaction-foundation-explore-step-20260825-114915-963.json` |
| KeyTap | NIKKE lobby→終了確認modalをEsc 1回で遷移。索引再実行はAI 0、`TransitionObserved=true`、10.027秒 | `keytap-source-lobby.png`、`keytap-after.png`、`host-keytap-known-ai0.summary.json` |
| Scroll | NIKKEランキングをwheelで上下移動。索引再実行はAI 0、`TransitionObserved=true`、10.091秒 | `ranking-moved.png`、`ranking-scroll-after.png`、`host-ranking-scroll-known-ai0.summary.json` |
| Drag | NIKKEランキングをdown→move→upで約350px移動。索引再実行はAI 0、`TransitionObserved=true`、10.033秒 | `drag-before.png`、`drag-after.png`、`host-ranking-drag-known-ai0.summary.json` |
| WaitStable | 早い安定候補で打ち切らず、操作後10秒間の再観測を継続。後半に別構造へ変われば古い安定候補を破棄する | `host-keytap-known-ai0.summary.json`、`host-ranking-scroll-known-ai0.summary.json`、`host-ranking-drag-known-ai0.summary.json` |
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

## 判定

NIKKEで反応を持つClick、KeyTap、Scroll、Dragはゲーム内効果まで確認した。HoverはNIKKEの非反応を`Stayed`のまま保存し、ゲーム共通GameLabで受理を確認した。gameが受理しない操作を成功へ丸めていない。
