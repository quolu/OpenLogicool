# Nano CDC liveness repair／shop monitored replay（2026-08-29）

## 結論

firmware 1.1.2の自己回復はmain loopが動いている場合だけ成立し、Arduino USB／CDC call内でblockするとsoftware timerへ戻れない欠陥があった。firmware 1.1.3はmain loop全体をAVR hardware watchdogで監視し、2秒以上戻らない場合にUSBを自己再列挙する。actionの自動再送は行わない。

Host側では、物理効果後のACK timeoutで`DispatchFailed`になったactionをdynamic tool successとしてCodexへ返していた。terminal action failureをtool failureとして返し、同じrunの後続actionと成功完了を拒否するよう修正した。Durable AttemptのOutcomeUnknownは消去せず、次dispatch拒否を維持する。

## 再現

- shop AI 0をNIKKEタイトル画面から誤開始したrunは2 step後に停止。アプリcaptureで開始page不一致を確認した。
- アプリ内Codexはタイトル→ロビーへ復帰し、報酬overlayを受取せずBackで閉じた。
- 一般ショップの無料購入確認をキャンセル後、ショップを閉じるBackでNano ACK timeoutが発生した。
- run evidenceはactionの物理効果後に未解決Attemptが残り、次のBackが「未完了のprobe」で拒否されたことをstack付きで記録した。
- Host終了後の独立`serial-hid-test`を時間を空けて2回実行し、どちらもCOM8のHello timeout。候補列挙だけは成立した。

## 修正

- firmware version: 1.1.3。
- `setup()`で`WDTO_2S`を有効化し、正常main loopだけが`wdt_reset()`する。
- 既存のreleasePending 1秒watchdogは維持し、正常loop内のfail-closed回復を先に行う。
- Host dynamic toolは`NoCandidate`／`AdmissionStopped`／`DispatchFailed`／`Paused`／`Abandoned`をterminal failureとして返す。
- terminal failure後の追加actionと、既存route revisionを根拠にした偽Completedを拒否する。

## 実機受入

- official build: 6410 bytes、RAM 262 bytes。
- verify付きflash／CDC・keyboard・mouse再列挙: 成立。
- firmware hex SHA-256: `75e10ea4f0330f5cabb8cc2a4798cf70a109741d3659cc24ede6a304ec2cfb1c`。
- flash直後100 session: 667ms、failure 0、firmware 1.1.3。
- 長時間shop monitored run後100 session: 674ms、failure 0、firmware 1.1.3。
- shop monitored replay: 19 step、AI 1、route revision 21→25、Completed。
- 購入結果: 無料クレジット60,000、費用0更新1回、無料コアダスト30、40% SALEミシリス共用コンソール10個／ボディラベル2,400、デイリー無料パック。ウィークリー無料パックは開始時点でSOLD OUT。
- 有償通貨・現金・非無料商品・募集・戦闘: 0。

## 検証

- focused Host: 4件green。
- related Host: 253件green。
- firmware native test／Arduino build: green。
- follow-up最終full regression: 22 test project・1259件green・failed 0・skipped 0。修正完了後に一回だけ実行した。

## 次の実測

同じreset分の無料商品は監視runで消費済みのため、shop routeのAI 0再生は次回日次reset後に実行する。これはroute失敗ではなく、同一商品を同日再購入できない外部状態による待機である。
