# OpenLogicool knowledge index

- [Nanoの無入力leaseと受信済み応答の時間切れ](openlogicool/nano-idle-lease-and-response-20261009.md) — 二つの停止条件を実機再現。Host修理は導入・実機確認済み、firmware修理は基板のdouble-resetによる書込み待ち。取得日: 2026-10-09、確度: 記載条件ごとに区別。

- [Bot通知の共通配達口と作業中への差し込み](openlogicool/codex-bot-delivery-provider-20261009.md) — キュー受付と同じターンへの差し込みの違い、Aitermの正規API、約30分の遅延と作業中の即時受信の実測。取得日: 2026-10-09、確度: 作業中は確認済み／変更後の待機中の遅延は未確認。

- [Botの詰まりの配達と納品画面の数量OCR](openlogicool/bot-assistance-delivery-20261009.md) — 公開配達CLI・公式受付と読了の区別、会話引き継ぎ、数量の領域OCR、自動登録と消費の区別。取得日: 2026-10-09、確度: 受付・引き継ぎ・待機中会話の自動起動・Nano納品は確認済み。

- [IMEモード状態の起動時押下誤認](openlogicool/ime-startup-held-20261009.md) — 0xF3がスキャンコードへ変換されてもIME状態を保持し、Bot再開を妨げた実測と修理。Windows SDKの定義を照合。

- [Jevによる画面種類の分類](openlogicool/typesafe-screen-routing-2026-10-08.md) — テキスト専用仕様と実OCR3例の分類。OCR破損は不明で、画像からの再読取と製品接続は未実装。

- [マビノギモバイルの回復・食事調査](openlogicool/mabinogi-recovery-food-2026-10-08.md) — 使用キー、食事前後のHUD比較、窓の移動・3サイズへの対応、白20％の包帯条件、未送出修理と再戦。取得日: 2026-10-08、確度: 食事効果・F1/F2送出・両スロット5→4は確認済み／白の意味と治療との因果・ポーション後のHP回復・クリアは未確認

- [Codex App ServerのGUI起動とUTF-8 stdio](openlogicool/codex-gui-stdio-encoding-2026-10-03.md) — コンソールなしのCP932誤読、BOMなしUTF-8明示、修理前後と導入後GUIの実測。取得日: 2026-10-03、確度: 確認済み（Microsoft一次資料＋Windows native）

- [Foundry Local 0.10.3 vision wire contract](openlogicool/foundry-local-vision-wire-2026-08-24.md) — Responses APIの`message`＋`image_data`＋`media_type`＋SSE契約、誤経路の実測、不明時no-fallback境界。取得日: 2026-08-24、確度: 高（Microsoft公式sample＋Windows実機）

- [STEP 0 Web Reference Policy（2026-08-24）](openlogicool/step0-web-reference-policy-2026-08-24.md) — GameWith利用規約／robotsとMarkdown保存境界、Web仮説をgame内検証へ従属させる契約
- [NIKKE日課 STEP 0 SummaryOnly（2026-08-24）](openlogicool/nikke-daily-gamewith-summary-2026-08-24.md) — GameWith日課候補、午前5時更新、通常デイリーとMission Passの分離、game内で確定した入口・基地防御報酬`0/1→1/1→10/100`。取得日: 2026-08-24、確度: Web仮説は参考／game内事実は確認済み
- [OpenLogicool feasibility research — G13/G600 on Windows](openlogicool/feasibility-2026-08-14.md) — 既存OSS、公開プロトコル、Windows実装境界、ブロッカー、最小実験、推奨試作。取得日: 2026-08-14、確度: 高（実機情報＋一次資料。一部は実機未検証）
- [Primary-source manifest](openlogicool/raw/source-manifest.md) — 調査に使用した一次資料URL、用途、ライセンス上の扱い。取得日: 2026-08-14、確度: 高
- [G600 onboard write protocol 公開実装調査](openlogicool/g600-write-protocol-2026-08-15.md) — 公開実装は全員 F3/F4/F5 へ 154-byte 直書き（F6 不使用）、write は settle+retry+fresh handle が要る運用知、incident の原因訂正。取得日: 2026-08-15、確度: 高（一次コード3実装+descriptor）
- [Serial HID firmware toolchain調査](openlogicool/serial-hid-toolchain-2026-08-23.md) — Arduino CLI 1.5.1、SparkFun AVR 1.1.13、Arduino AVR 1.8.8、Pro Micro 5V / 16 MHz FQBN、HID API、checksum、固定compile。取得日: 2026-08-23、確度: 高（公式catalog＋導入後source＋compile）
- [Serial HID Windows discovery調査](openlogicool/serial-hid-windows-discovery-2026-08-23.md) — SetupAPI COM interface列挙、PnP device instance ID、SparkFun VID/PID、SerialPort partial readの実装根拠。取得日: 2026-08-23、確度: 高（Microsoft公式API＋導入済みboard定義）
- [G13 LCD Windows標準HID write調査](openlogicool/g13-lcd-windows-write-2026-08-23.md) — 960-byte framebuffer＋32-byte header、標準HidUsbの`WriteFile`でLCD反映、write後もG1 down/up・drop 0、driver差替え不要。取得日: 2026-08-23、確度: 高（Microsoft公式仕様＋G13公開一次コード＋Windows実機）

実測で確定した仕様知識（正本は docs/ 側・ここは索引のみ）:

- Serial HID Output campaign: [Exit Assessment](../docs/serial-hid-output-exit-assessment.md)／[運用手順](../docs/serial-hid-output-operation.md)（確認済み。SparkFun Pro Micro ATmega32U4 5V / 16MHz、firmware 1.0.0、Windows 11 x64、6KRO、G13／G600共通経路、no fallback、hard-kill release 148.6321ms、dispatch p99 3.425ms）
- G600 onboard profile 154-byte layout: [docs/probes/g600-profile-decode-2026-08-15.md](../docs/probes/g600-profile-decode-2026-08-15.md)（強い推定・独立整合3本）
- G600 raw report 0x80 全control対応: [docs/probes/g600-input-map-2026-08-15.md](../docs/probes/g600-input-map-2026-08-15.md)（確認済み）
- WGC/DXGI/GDI capture backend成立と WinRT interop の罠: [docs/probes/capture-backend-matrix-2026-08-15.md](../docs/probes/capture-backend-matrix-2026-08-15.md)（確認済み）
- LGS 9.04.49 profile XML スキーマ（Cassandra namespace・shiftstate 6層・task語彙17種）: [docs/lgs-parity-inventory-2026-08-15.md](../docs/lgs-parity-inventory-2026-08-15.md)（確認済み）
- [NIKKE は SendInput 合成入力を受理しない](openlogicool/nikke-sendinput-rejection-2026-08-22.md) — fast path 送出成立＋ゲーム内反映なしの実測で確定。anti-cheat の注入入力フィルタと判定、方式A（onboard 直書き）が対応経路。取得日: 2026-08-22、確度: 確認済み（実機）
