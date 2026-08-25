# t09 10の基盤機能 最終判定

判定日: 2026-08-25

## 結論

10の基盤機能は完成した。NIKKE固有のstate、target、文字列、座標、遷移、手順を製品コードへ入れず、Windows adapter、認識、操作、安定待機、判定、遷移保存、逐次Screen Index、Host／UI portを一経路へ接続した。

Phase 12全体のzero-seed複数target探索は`t08`の未確認を維持し、10の基盤機能Exitへ混ぜていない。

## 最終実測

- DiscoverTargets: `kind=icon`の局所visual targetを初回AI 1回で保存し、Foundry停止中のAI 0回再実行で`Moved`。`host-visual-image-discover-ai1.json`、`host-visual-image-known-execute-ai0.json`。
- Hover: NIKKEの保存済みbuttonは`Stayed`。GameLab OpenEventは白→青、索引保存、AI 0回再実行で`Stability=Stable`、`TransitionObserved=true`、`DestinationMatched=true`。10.055秒、67観測。`gamelab-hover-known-ai0.summary.json`。
- KeyTap: NIKKE lobby→終了確認modal。AI 0回再実行で`TransitionObserved=true`。10.027秒、24観測。OCR state IDは厳密不一致のため`DestinationMatched=false`。`host-keytap-known-ai0.summary.json`。
- Scroll: NIKKEランキング内容移動。AI 0回再実行で`TransitionObserved=true`。10.091秒、20観測。`host-ranking-scroll-known-ai0.summary.json`。
- Drag: NIKKEランキング内容を約350px移動。AI 0回再実行で`TransitionObserved=true`。10.033秒、20観測。`host-ranking-drag-known-ai0.summary.json`。
- WaitStable: 早い安定候補では終えず、操作後10秒間の画面認識を継続する。
- Compare／LearnTransition: `Moved`／`Stayed`／`Undetermined`を分離し、静止WGCの同一frameによる`NoChange`も安定窓付きで保存する。

## 途中で根治した基盤欠陥

1. 静止WGCが新frameを通知しない正常状態をtimeoutにしていた。最後の有効frameだけを再観測し、最小化／resize等の明示faultでは再利用しない。
2. 同じframe番号の静止`NoChange`をLearnTransitionが拒否していた。`NoChange`だけ同一番号を許し、観測回数と安定時間を必須にした。
3. Hover表示差へ保存画像同一性の許容差を使っていた。実PNGの局所patch差`0.828125`を測り、Hover反応だけを`0.5`で判定した。
4. 常時アニメーションやOCRの一文字揺れを画面遷移にしていた。位置付き軽量文字距離、全画面visual差、複数構造差で抑制した。
5. `Moved`だけで`DestinationMatched=true`にしていた。再探索用の`TransitionObserved`と、state ID厳密一致の`DestinationMatched`を分離した。
6. 途中の安定画面とcapture fault前のWGC cacheを後半へ持ち越していた。後続構造変化／faultで候補とcacheを破棄する負例を追加した。

## 検証

- 変更直結関連test 385件green。
- `dotnet test OpenLogicool.sln --no-restore`: 1202件green、失敗0。
- `git diff --check`: 通過。
- SendInput 0、Computer Use dispatch 0、fallback 0、blind retry 0。
