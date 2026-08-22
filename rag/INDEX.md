# OpenLogicool knowledge index

- [OpenLogicool feasibility research — G13/G600 on Windows](openlogicool/feasibility-2026-08-14.md) — 既存OSS、公開プロトコル、Windows実装境界、ブロッカー、最小実験、推奨試作。取得日: 2026-08-14、確度: 高（実機情報＋一次資料。一部は実機未検証）
- [Primary-source manifest](openlogicool/raw/source-manifest.md) — 調査に使用した一次資料URL、用途、ライセンス上の扱い。取得日: 2026-08-14、確度: 高
- [G600 onboard write protocol 公開実装調査](openlogicool/g600-write-protocol-2026-08-15.md) — 公開実装は全員 F3/F4/F5 へ 154-byte 直書き（F6 不使用）、write は settle+retry+fresh handle が要る運用知、incident の原因訂正。取得日: 2026-08-15、確度: 高（一次コード3実装+descriptor）
- [Serial HID firmware toolchain調査](openlogicool/serial-hid-toolchain-2026-08-23.md) — Arduino CLI 1.5.1、SparkFun AVR 1.1.13、Arduino AVR 1.8.8、Pro Micro 5V / 16 MHz FQBN、HID API、checksum、固定compile。取得日: 2026-08-23、確度: 高（公式catalog＋導入後source＋compile）

実測で確定した仕様知識（正本は docs/ 側・ここは索引のみ）:

- G600 onboard profile 154-byte layout: [docs/probes/g600-profile-decode-2026-08-15.md](../docs/probes/g600-profile-decode-2026-08-15.md)（強い推定・独立整合3本）
- G600 raw report 0x80 全control対応: [docs/probes/g600-input-map-2026-08-15.md](../docs/probes/g600-input-map-2026-08-15.md)（確認済み）
- WGC/DXGI/GDI capture backend成立と WinRT interop の罠: [docs/probes/capture-backend-matrix-2026-08-15.md](../docs/probes/capture-backend-matrix-2026-08-15.md)（確認済み）
- LGS 9.04.49 profile XML スキーマ（Cassandra namespace・shiftstate 6層・task語彙17種）: [docs/lgs-parity-inventory-2026-08-15.md](../docs/lgs-parity-inventory-2026-08-15.md)（確認済み）
- [NIKKE は SendInput 合成入力を受理しない](openlogicool/nikke-sendinput-rejection-2026-08-22.md) — fast path 送出成立＋ゲーム内反映なしの実測で確定。anti-cheat の注入入力フィルタと判定、方式A（onboard 直書き）が対応経路。取得日: 2026-08-22、確度: 確認済み（実機）
