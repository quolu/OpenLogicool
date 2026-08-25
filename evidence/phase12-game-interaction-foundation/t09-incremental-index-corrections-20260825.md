# 増分ボタン索引・OCR裁定の実測更新（2026-08-25）

## オーナー裁定

- ボタン探索は、現在ページに保存済みボタンがない時と、保存済みボタンを実行して10秒観測しても正常遷移しなかった時だけ起動する。
- AI座標やOCR文字列の差だけで候補を誤りと判定しない。送出後のページ遷移を判定根拠にする。
- 全OCR照合は位置と軽い文字距離を使い、完全一致を要求しない。
- 後のOCRがより自然なら、StateId／ActionId／座標／遷移／旧証拠を維持して保存文字列を更新する。

正典は `AGENTS.md` 8項と `docs/development-plan.md` §6.13.3.1。

## 実測

- 画像ボタンの初回発見→destination保存→Foundryアンロード後のAI0再実行は成立した。`host-visual-image-discover-ai1.json` と `host-visual-image-known-execute-ai0.json`。
- Hoverは索引保存とAI0送出まで成立したが、再実行が `Stayed` だったためゲーム内受理は未確認。`host-hover-friend-known-ai0.json`。
- Messenger候補は10秒観測しても遷移を確定できず、destination未確定のまま保持した。`host-messenger-open-ai1.json` と `host-messenger-rediscover-ai1.json`。
- Scrollは製品runtime・索引parameter・Nano送出経路を実装したが、NIKKEロビーでは候補不成立で実受理未確認。`host-scroll-live-ai1.json`。
- qwen3-vl-8bは公式Foundry経路でcache取得まで完了したが、比較attemptで遷移を成立できずprovider採用していない。

NIKKE固有文字列・座標は本evidenceとSQLite実測DBだけにあり、製品コードへ入れていない。Computer Useは使用していない。
