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

## 製品修正

- 保存actionを試す前にFoundry Localを解決しない。AI探索が必要になった時だけWindows vendor adapterが解決する。
- elevated gameを含む前面化は、目的実行が所有する同じNano sessionからWindows taskbarの意味buttonを一回押す。別processでNanoを開き直さない。
- taskbar／Alt+Tab実装はWindows固有ファイルへ分離し、共通compositionはadapter呼出しだけを持つ。
- terminal fault後の終了処理はresident sessionの`Dispose`へ一本化し、二重`ALL_UP`で元のfaultを上書きしない。
- AIの目的指定は一件制限を維持するが、最終goal文字列との類似だけで中間navigation stepを捨てない。

## 判定

- アプリ固定target、launcher起動、前面化、WGC観測、目的入力入口: **確認済み**
- 日課情報の取得: **未成立**
- 阻害条件: NIKKE.exeの再現クラッシュとNano terminal fault。物理Nano再接続と、NIKKEが安定して動作する実機時間が必要。
- 報酬受取、資源消費、課金、戦闘: **実行0**
- 最終full regression: **22 test project・1232件green、失敗0、skip 0**
