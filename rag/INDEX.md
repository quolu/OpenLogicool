# OpenLogicool knowledge index

- [OpenLogicool feasibility research — G13/G600 on Windows](openlogicool/feasibility-2026-08-14.md) — 既存OSS、公開プロトコル、Windows実装境界、ブロッカー、最小実験、推奨試作。取得日: 2026-08-14、確度: 高（実機情報＋一次資料。一部は実機未検証）
- [Primary-source manifest](openlogicool/raw/source-manifest.md) — 調査に使用した一次資料URL、用途、ライセンス上の扱い。取得日: 2026-08-14、確度: 高

実測で確定した仕様知識（正本は docs/ 側・ここは索引のみ）:

- G600 onboard profile 154-byte layout: [docs/probes/g600-profile-decode-2026-08-15.md](../docs/probes/g600-profile-decode-2026-08-15.md)（強い推定・独立整合3本）
- G600 raw report 0x80 全control対応: [docs/probes/g600-input-map-2026-08-15.md](../docs/probes/g600-input-map-2026-08-15.md)（確認済み）
- WGC/DXGI/GDI capture backend成立と WinRT interop の罠: [docs/probes/capture-backend-matrix-2026-08-15.md](../docs/probes/capture-backend-matrix-2026-08-15.md)（確認済み）
- LGS 9.04.49 profile XML スキーマ（Cassandra namespace・shiftstate 6層・task語彙17種）: [docs/lgs-parity-inventory-2026-08-15.md](../docs/lgs-parity-inventory-2026-08-15.md)（確認済み）
