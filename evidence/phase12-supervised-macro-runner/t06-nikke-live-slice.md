# t06 NIKKE実ゲーム可逆slice

判定日: 2026-08-25

## 結論

保存済みLearning Route「ロビー→部隊編成→ロビー」の2stepを、NIKKE実ゲームとNano Serial HIDで一巡し、Completedまで成立した。

各stepは、操作前の保存ページ／action照合、Nano一回入力、10秒のWaitStable、Compare=Movedを同じ10の基盤機能で実行した。教師付きmacro固有のOCR厳密一致、pointer移動後再探索／補正、wall-clock 500ms gate、destination IDによる操作拒否は使っていない。

## 実測

- step 1: ロビーの保存action「部隊」をNano click 1回。10.221秒、24観測、23 stable frame、8.907秒連続安定、Moved、部隊編成destination一致。
- step 2: 部隊編成からNano Esc 1回。10.101秒、19観測、12 stable frame、8.822秒連続安定、Moved、ロビーdestination一致。
- journal: proposal 2、Automation approval 2、dispatch 2、dispatch-result 2、confirmation 2。
- SendInput dispatch 0、Computer Use dispatch 0、retry 0、fallback 0。
- 最終画面はロビー。

機械可読要約は[t06-nikke-live.summary.json](t06-nikke-live.summary.json)。raw evidenceはprobe-output/supervised-macro-live-20260825-192745-622.json、SHA-256は59a2814ac8dbd5e6be26172c7e5e6bfe755b5f435bc4bb06fd5e0bb85a1b472e。

## 未回収実装の移行

古い教師付きruntimeから次を撤去した。

1. pointer移動後のOCR再探索、座標補正、再確認。
2. OCR処理時間だけで超過し得るwall-clock 500ms拒否。
3. LocatorRevision厳密一致による保存action拒否。
4. 操作後destination ID一致を次step条件にする判定。
5. 連続frame一致を要求する操作前の重複安定確認。

残した境界は、対象window／Nano不在、禁止risk、現在ページ／保存actionを10秒で一度も観測できない場合、Nano dispatch faultだけである。

途中の診断runが入力後OutcomeUnknownになったDBは未解決Attemptを保持し、新runを正しく拒否した。最終実証は現行packageを新規DBへimportし、診断履歴と混ぜずに行った。

## 検証

- 変更直結関連test: Domain 101、Playbooks 171、Persistence 50、Host 201、Desktop 92、Probe 61。合計676件green。
- dotnet test OpenLogicool.sln --no-restore: 1208件green、失敗0。
- git diff --check: 通過。
