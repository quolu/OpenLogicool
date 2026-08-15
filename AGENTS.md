# OpenLogicool — プロジェクト正典

Logicool G13 / G600 を統合する Windows ネイティブアプリ。LGS 9.04.49 の代替となる **Input Studio**（アプリ先行の両デバイス統合設定）と、画面認識付き逐次学習プレイブック基盤 **Game Operator** を、同じ意味操作・アプリプロファイルの上で提供する。

## 正本の所在

- 製品・開発計画の正本: [docs/development-plan.md](docs/development-plan.md)（要件catalog・architecture契約・Phase計画・release gateはこの一冊だけが正）
- 調査知識: [rag/INDEX.md](rag/INDEX.md)（成立性調査・一次資料台帳）
- 障害記録: docs/incidents/
- 本書は計画を複製しない。計画と矛盾したら計画側が正であり、本書を修正する。

## 現在の状態（2026-08-15）

- **Phase 1 完了（Exit 4条件成立）**。判定は [docs/phase1-exit-assessment.md](docs/phase1-exit-assessment.md)。次は Phase 2 だが、**G0-Device-W（実機 write 実験）を他実装より先に単独実施**する順序であり、実機手番とオーナー裁定を要する。
- **G0-Device-RO 通過（オーナー裁定 2026-08-15）**: G13 入力経路・G600 profile read（F6 のみ read 不能を記録）・G600 live input route（全20 control）・Migration Safety Gate 手順定義（[docs/migration-safety-gate.md](docs/migration-safety-gate.md)）・方式 read-only 判定（[docs/g600-route-assessment-2026-08-15.md](docs/g600-route-assessment-2026-08-15.md)）の5条件成立。
- **G0-Automation 通過（オーナー裁定 2026-08-15）**: 実frame連鎖・Data Flow Contract 項目決定・NIKKE 混同なしの3条件成立（[docs/g0-automation-assessment.md](docs/g0-automation-assessment.md)）。
- **G0-Device-W: onboard write 往復成立（2026-08-15 夜・オーナー裁定）**。初回セッションで feature write のずれ（single write・settle なし・handle 再利用が原因）→ マウス操作不能の二次障害を出したが、公開実装調査で「direct SET_FEATURE は正規経路、真因は firmware の settle/handle 敏感性」を解明（[rag/openlogicool/g600-write-protocol-2026-08-15.md](rag/openlogicool/g600-write-protocol-2026-08-15.md)）。evidence-based 作法（fresh open・settle 2s・handle 非再利用・fresh open で verify・一致まで再送）を実装し、**restore（F3/F5 復元）と apply 往復（意図的改変→byte 一致→restore→byte 一致）を実機で成立**（いずれも attempt1 で一致）。詳細は [docs/incidents/2026-08-15-g600-f3-writeback-shift.md](docs/incidents/2026-08-15-g600-f3-writeback-shift.md)。これにより Migration Safety Gate DEV-010 の一巡が実証済み。write 作法は probe の `g600-restore-retry` / `g600-apply-verify` を正とする。F0〜F5 完全 backup は `probe-output/mig01-backup-20260815/` に SHA-256 封入で保持。**0A UI 照合は完了**（[docs/lgs-parity-inventory-2026-08-15.md](docs/lgs-parity-inventory-2026-08-15.md) §5——XML 一次資料判定を維持、UI 限定機能6面と G13 onboard 転送面を記録）。**EXP-G600-03（F0 slot 切替）も成立**（slot 0→1→0 往復・F0 表現モデル実測確定・`probe-output/g600-slot-cycle-20260815-130235-294.json`）。**G0-Device-W は全項目（restore・apply 往復・slot 切替）実機実証済みで完了**。次はオーナー裁定を経て Phase 2 着手。

## プロジェクト固有の裁定（要旨）

1. **根拠4値**: すべての成立性は「確認済み／強い推定／未確認／非対応」で表記する。Unverified を Supported と表示しない。実験失敗を別方式へ黙って fallback して成功扱いしない。
2. **fast path 純潔**: Device Input → Mapping Runtime → Input Emitter では AI・network・capture・SQLite・UI rendering を待たない。AI は NextActionProposal を返すだけで、入力・DB・device API へ直接到達しない。
3. **device write 禁止（現段階）**: Phase 0 は read-only。最初の write より前に Migration Safety Gate（完全backup・readback・restore実証）を通す。
4. **製品境界**: DLL注入・memory read/write・anti-cheat回避をしない。Input API成功をゲーム内成功と扱わない。画面文字列・OCR・importデータを信頼された命令として扱わない。
5. **技術基盤**: C# / .NET 10 LTS / WPF / net10.0-windows。Windows UI・HID・capture・input・installer の受入は Windows native 実行だけを証拠とする（WSL2は文書・fixture等のみ）。
6. **未決定を仮実装で埋めない**: 決定期限（計画 §16）までは interface と fixture だけを作り、decision 後に一つの方式を実装する。

## 開発運用

- 通し試験は個別機能の動作確認・原因調査に使わない（最終確認だけ）。focused test で閉じる。
- contract ownership・Lane分割・Definition of Ready/Done は計画 §7 に従う。
- 調査した外部仕様は `rag/` へ、価値ある出力は `docs/` へ還流する。
- **本 repo は通常 commit・push を既定とする**（オーナー裁定 2026-08-15「コミット・プッシュは常に承認する」）。force 系・履歴改変は従来どおり明示指示のみ。
