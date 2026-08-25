# 増分ボタン索引・OCR裁定の実測更新（2026-08-25）

## オーナー裁定

- ボタン探索は、現在ページに保存済みボタンがない時と、保存済みボタンを実行して10秒観測しても正常遷移しなかった時だけ起動する。
- AI座標やOCR文字列の差だけで候補を誤りと判定しない。送出後のページ遷移を判定根拠にする。
- 全OCR照合は位置と軽い文字距離を使い、完全一致を要求しない。
- 後のOCRがより自然なら、StateId／ActionId／座標／遷移／旧証拠を維持して保存文字列を更新する。

正典は `AGENTS.md` 8項と `docs/development-plan.md` §6.13.3.1。

## 実測

- 画像ボタンの初回発見→destination保存→Foundryアンロード後のAI0再実行は成立した。`host-visual-image-discover-ai1.json` と `host-visual-image-known-execute-ai0.json`。
- HoverはNIKKEの保存済みbuttonで`Stayed`であり、その情報を壊していない。hover対応GameLabのOpenEventでは白→青の表示変化、索引保存、Foundry停止中のAI0再実行が`Stable`＋`Moved`で成立した。`host-hover-friend-known-ai0.json`、`gamelab-hover-before.png`、`gamelab-hover-after.png`、`gamelab-hover-known-ai0.summary.json`。
- Messenger候補は10秒観測しても遷移を確定できず、destination未確定のまま保持した。`host-messenger-open-ai1.json` と `host-messenger-rediscover-ai1.json`。
- KeyTapはNIKKE lobby→終了確認modal、Scroll／DragはNIKKEランキングの内容移動でゲーム内受理を確認した。3操作とも索引保存後、Foundry停止中のAI0再実行が`TransitionObserved=true`で成立し、遷移確認はそれぞれ10.027秒、10.091秒、10.033秒継続した。`host-keytap-known-ai0.summary.json`、`host-ranking-scroll-known-ai0.summary.json`、`host-ranking-drag-known-ai0.summary.json`。
- qwen3-vl-8bは公式Foundry経路でcache取得まで完了したが、比較attemptで遷移を成立できずprovider採用していない。

NIKKE固有文字列・座標は本evidenceとSQLite実測DBだけにあり、製品コードへ入れていない。Computer Useは使用していない。

## 基盤欠陥の根治

- WGCが静止画面で新frameを通知しない正常状態では、Windows adapterが最後の有効frameを再観測へ使う。最小化／resize等の明示faultでは再利用しない。
- 静止画面の`NoChange`は同じWGC frame番号を許し、3回以上の観測と安定時間で成立させる。`Moved`／`Novel`は従来どおり新しいframe番号を要求する。
- Hover反応は保存画像の同一性許容差とは分離し、実測した局所patch差`0.828125`を識別できる感度で判定する。
- `TransitionObserved`と`DestinationMatched`を分離する。前者だけを再探索条件に使い、後者はexpected／observed state IDの厳密一致だけを表す。
- 途中で成立した安定画面は、10秒の後半で別構造へ変化した時点で破棄する。capture fault後のWGC cacheも破棄し、次の静止通知へ持ち越さない。
