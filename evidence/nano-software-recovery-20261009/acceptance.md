# Nanoの無応答を物理抜き挿しせず復旧

2026-10-09、Windows native。オーナーの「抜き差ししないで解決して欲しい」に従って実施した運用復旧。

## 実施と結果

1. SetupAPI/PnPでNano本体・COM3・keyboard・mouseが存在し、すべてStatus=OKであることを確認。HELLOはBot起動と正規接続診断の両方で時間切れだった。
2. 昇格済みの実行環境で、Nano本体一台のexact instance IDを照合し、Windows標準`pnputil /restart-device`を実行。PnPは成功、同じCDC interfaceがCOM3へ復帰した。
3. 正規`serial-hid-test --repeat 1`が24msで成功。firmware 1.1.3のREADYと全解放を確認した。物理抜き挿し・再flash・ゲーム入力の自動再送は行わない。
4. CLIからBotを明示的に再開。起動時HeldCount=0、無入力3,027msで一時停止解除。6,210msで右下コンパスのSpace、6,357msで食事BをNanoSerialHidから各1回送出。HP100％と250ms前後の回復観測を継続した。
5. OSの0xF3はdown、scan 0x29のままでも、修正版Botの押下数は0を維持した。IMEモードを起動時押下として扱わない修理の実機再開確認も成立した。

機器再起動は応答を回復させる運用操作であり、無応答になった根本原因の特定とは扱わない。Windows側とfirmware側のどちらが起点かは未特定。再発時は別の入力経路へ切り替えず、停止記録とPnP状態を確認する。

根拠: `device-before.json`、`pnp-restart.txt`、`connection-test.json`、`bot-start.json`、`bot-status.json`、`live-input-events.jsonl`、`ime-os-state.json`。前の修理の関連22件・Host505件成功と成果物一致は[先行記録](../bot-monitor-20261009/acceptance.md)。今回は製品コードを追加変更しない。

一次資料: [Microsoft PnPUtilの機器再起動](https://learn.microsoft.com/en-us/windows-hardware/drivers/devtest/pnputil-command-syntax)。USB制御全体を再起動せず、確認済みのNano本体一台へ限定した。

Botと5分ごとの監視を再開済み。録画原本・マクロ・キー割当・回復設定を保持する。
