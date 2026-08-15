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
- **G0-Device-W 初回実機セッション（2026-08-15）: 不通過・device write 再凍結**。EXP-MIG-01 段2で device の feature write が payload をずれて格納する事象を検出（direct SET_FEATURE・LGS 純正経路の両方で再現、power cycle 非解消）。詳細と最終状態は [docs/incidents/2026-08-15-g600-f3-writeback-shift.md](docs/incidents/2026-08-15-g600-f3-writeback-shift.md)。**onboard write は経路を問わず全面凍結**。解除条件は F6 コマンド系（LGS 正規 write protocol）の解明と F3/F5 復元実証。F0〜F5 完全 backup は `probe-output/mig01-backup-20260815/` に SHA-256 封入で保持。次の一手は公開実装（libratbag 等）の protocol 調査（read-only・実機不要）。0A UI照合（LGS 画面突合）は実機手番待ち。

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
