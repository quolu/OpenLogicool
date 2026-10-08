# Bot再開時の手入力誤判定と現在地

2026-10-09、Windows native。オーナーの「ゲーム再開。監視してくれ」に従い、Botの状態と入力・回復観測を確認した。

## 発見・修理

起動直後、実入力0件でHeldCount=1となり、無入力約9分後も一時停止していた。コンパスを検出しても送出0回。`before.json`と`stale-hold-events.jsonl`が根拠。

Windowsの実測では0xF3がdown、scanは0x29、逆変換も0xF3。SDKのIME用モードVK_DBE_SBCSCHARと同じ値で、起動時の実キー押下として読み込まれていた。起動時の取込みだけIMEモード0xF0〜0xFBを外し、起動後の実down/up追跡は保持した。根拠は[調査記録](../../rag/openlogicool/ime-startup-held-20261009.md)。

修理前の再現試験12件が失敗、修理後の関連22件とHost全505件が成功。開発版へ反映済み。Host DLLはRelease成果物と一致: `2850B4BF6B4A02FF4400D413E4B839DF3F53759677A016F2B3491AE1BC770EA4`。

## 通信障害と後続の復旧

更新後のBot起動と正規の`serial-hid-test --repeat 1`の両方で、COM3のNanoがHELLOへ応答せず時間切れ。入力前に停止し、別の入力経路や自動再送は使わない。記録は`restart.json`、`after.json`、`serial-diagnostic.json`。通信障害の原因は未特定。修理後の実機再開は未確認。

NanoのUSB抜き挿しを決裁箱K-3YHZG5で依頼した。回答後は接続診断→CLIからBot開始→初期HeldCountと3秒後の再開→コンパスSpace送出・HP観測の継続を確認する。録画原本・マクロ・キー割当・回復設定は変更しない。

その後、オーナーから抜き挿しせず解決する指示を受け、Windows標準のNano一台の機器再起動で通信を復旧した。修正版BotはHeldCount=0で再開し、Spaceと回復観測を実測できた。詳細は[後続記録](../nano-software-recovery-20261009/acceptance.md)。通信障害の発生原因自体は未特定。

5分ごとの監視をこの会話へ設定済み。現在はアプリとBotを再開している。
