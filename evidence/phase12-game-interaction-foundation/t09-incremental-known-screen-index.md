# t09 目的駆動・逐次Screen Index／AIなし既知実行

判定日: 2026-08-25

## 結論

「目的に必要な未登録ボタンをAIで一度だけ発見し、ページ・座標・操作・行き先を逐次追記し、同じ操作の2回目はAIなしで実行する」製品Host基盤がNIKKE実機で成立した。

ページ内の全ボタン列挙は行わない。初回goalに対応した1件だけを保存する。

## 初回discover

証拠: `host-goal-discover-ai1.json`

- product entry: `OpenLogicool.Host game-index discover`
- 明示Game Policy: `--allow-explore`
- goal: `アリーナを開く`
- AI選択: `アリーナ` 1件
- AI call: 1
- indexed state: 2
- indexed action: 1
- action ID: `known-action:6f9ac84464ae0f0df21e59a6343fc453477892e63d284fa15e8cdb03cd0bf0c0`
- source page、normalized bounds、Click、destination pageをSQLiteへ保存
- `HostExplorerIntents` runtime接続済み

## 再起動復元

証拠: `host-known-index-reopen.json`

- Host processを終了後、同じSQLiteを再open
- state 2、action 1を復元
- `アリーナ`座標 `[0.5545955882, 0.6553884712, 0.0244485294, 0.0129490393]`
- destination stateを復元
- AI call: 0

## AIなし既知実行

証拠: `host-known-execute-ai0.json`

- 実行前にFoundry Local 4Bを公式CLIでunload
- product entry: `OpenLogicool.Host game-index execute`
- AI call: 0
- Windows OCR＋`LearnedSceneMatcher`でsource pageをKnownとして一意照合
- 保存actionを最新frameへ再束縛
- Nano Click dispatch 1
- Comparison: Moved
- expected destinationとobserved destinationが完全一致
- `DestinationMatched=true`
- 実行後はHost `game-index back`、AI call 0でアークへ復帰

## 構造

1. `IncrementalKnownScreenIndex.RememberControl`はdispatch前に今回の1ボタンだけを追記する。
2. `RememberDestination`は安定観測後に行き先だけを追記する。
3. ページanchorはWindows OCR複数frameの共通文字・近接位置から2件選ぶ。AI出力をanchorにしない。
4. `KnownScreenActionRuntime.ExecuteKnownAsync`はFoundry型を参照せず、WGC、Windows OCR、SQLite profile、Nanoだけを使う。
5. 未登録actionはKnown実行で明示エラーとなり、AIへ黙ってfallbackしない。
6. actionはrisk admission成功後・dispatch直前だけ保存する。destination未確定、Game Policy不許可、現行risk禁止のactionはKnown実行しない。

## 非対応

- icon-only controlの初回発見
- OCRで安定anchorを2件作れないページ
- 保存destinationと再観測destinationが一致しない操作の成功扱い
- 全ボタンの事前列挙

## 最終検証

- safety修正後 focused: 6 green
- solution full regression: 1156 green、失敗0
