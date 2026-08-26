# NIKKE 日課情報取得 live attempt（2026-08-26）

## 目的と制約

- アプリ側の固定プロフィール: `nikke`
- 利用者goal: 日課の情報を取得する
- 明示禁止: 報酬受取、資源消費、課金、戦闘
- 入力経路: Nano Serial HIDのみ。SendInput／Computer Useは0
- UIを介さない製品入口: `macro create --goal ...`

## 実測

1. NIKKE launcherの既知座標をAI 0・Nano click 1回で実行し、NIKKE.exeを起動した。
2. Windows taskbarの意味button `nikke - 1 個の実行中ウィンドウ`をUI Automationで特定し、Nano click 1回で前面化した。
3. WGCで2720x1197のタイトル画面と`TOUCH TO CONTINUE`を取得した。PNG SHA-256は`071abd8778a0fe7ff6e92866bd987c1c4b83a32c813fd71327cf19290c2e453a`。
4. `macro create`へgoalを投入したが、初回は保存actionを試す前のFoundry Local CLI解決で停止した。Foundry Local service自体はready、loaded Multimodal modelは`qwen3-vl-4b-instruct-cuda-gpu:2`だった。
5. 保存action前のFoundry必須解決をWindows vendor adapterで遅延化した。focused test 1件、関連AI 43件・Host 231件・architecture 8件はgreen。
6. NIKKE.exeはNano入力なしでもApplication Error 1000で再現クラッシュした。11:40、11:46、11:52の各回とも`KERNELBASE.dll`、例外`0xe06d7363`、fault offset`0x00000000000c187a`で一致した。二回目は起動77秒後、タイトル画面への入力0で発生したため、`TOUCH TO CONTINUE`クリックを原因とは判定しない。
7. 三回目は目的実行中にNano heartbeat sequence 18がtimeoutし、sessionは契約どおりterminal faultになった。自動再送／fallbackは0。NIKKEも同時刻帯に同一例外で終了した。
8. 後続調査で、Foundry Local 0.10.3 daemonのpaged memoryが約34GB、Windowsのfree virtual memoryが約2.3GBまで減っていた。HostとPowerShellも`OutOfMemoryException`になったため、同時刻帯のNIKKE crashを単独game defectとは判定しない。
9. 公式CLIのmodel unloadでdaemonのpaged memoryが約13GBから約4.8GBへ下がり、同じendpointのまま再loadできることを実測した。Windows vendor adapterへAI callごとのload／unloadを接続し、free virtual memoryは約27GBを維持した。
10. NIKKEを再起動し、製品HostのNano／WGC経路でMISSION→デイリーtabを開き、一覧を最下端までscrollした。現在値は`0/100`、残り約16時間、全11項目が未完了だった。

## 取得した日課

| point | 項目 | 進捗 |
|---:|---|---:|
| 20 | シミュレーションルームに1回挑戦する | 0/1 |
| 20 | 迎撃戦を1回クリアする | 0/1 |
| 20 | ニケの面談を1回実行する | 0/1 |
| 10 | 派遣を3回実行する | 0/3 |
| 10 | 基地防衛報酬を1回獲得する | 0/1 |
| 10 | まとめて殲滅を1回実行する | 0/1 |
| 10 | フレンドにソーシャルポイントを1回プレゼントする | 0/1 |
| 10 | キャンペーンを1回実行する | 0/1 |
| 10 | 隊員募集を1回実行する | 0/1 |
| 10 | タワーに1回挑戦する | 0/1 |
| 10 | 一般ショップでアイテムを1回購入する | 0/1 |

## 製品修正

- 保存actionを試す前にFoundry Localを解決しない。AI探索が必要になった時だけWindows vendor adapterが解決する。
- elevated gameを含む前面化は、目的実行が所有する同じNano sessionからWindows taskbarの意味buttonを一回押す。別processでNanoを開き直さない。
- taskbar／Alt+Tab実装はWindows固有ファイルへ分離し、共通compositionはadapter呼出しだけを持つ。
- terminal fault後の終了処理はresident sessionの`Dispose`へ一本化し、二重`ALL_UP`で元のfaultを上書きしない。
- AIの目的指定は一件制限を維持するが、最終goal文字列との類似だけで中間navigation stepを捨てない。
- 目的完了判定は意味action／まとまったOCR affordanceだけを使い、local OCRの一文字substring列を使わない。
- Foundry Local modelはAI call終了時に公式unload、次call直前に公式loadし、Windows commit枯渇を防ぐ。
- taskbar buttonはprocess名だけでなく実window titleでも照合する（`nikke_launcher`→`NIKKE`差をWindows adapterで吸収）。

## 判定

- アプリ固定target、launcher起動、前面化、WGC観測、目的入力入口: **確認済み**
- 日課情報の取得: **確認済み**
- 一つの長いgoalだけによる完全自動route: **未成立**。途中の誤候補は停止・復帰し、確認済み座標のNano操作でMISSION一覧へ到達した。
- 報酬受取、資源消費、課金、戦闘: **実行0**
- 最終full regression: **22 test project・1232件green、失敗0、skip 0**
