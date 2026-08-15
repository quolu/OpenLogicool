# OpenLogicool — プロジェクト正典

Logicool G13 / G600 を統合する Windows ネイティブアプリ。LGS 9.04.49 の代替となる **Input Studio**（アプリ先行の両デバイス統合設定）と、画面認識付き逐次学習プレイブック基盤 **Game Operator** を、同じ意味操作・アプリプロファイルの上で提供する。

## 正本の所在

- 製品・開発計画の正本: [docs/development-plan.md](docs/development-plan.md)（要件catalog・architecture契約・Phase計画・release gateはこの一冊だけが正）
- 調査知識: [rag/INDEX.md](rag/INDEX.md)（成立性調査・一次資料台帳）
- 障害記録: docs/incidents/
- 本書は計画を複製しない。計画と矛盾したら計画側が正であり、本書を修正する。

## 現在の状態（2026-08-15）

- 実装着手前。**許可済みは Phase 0（read-only調査・契約draft・UX prototype・GameLab prototype）だけ**。製品実装のGoは分野別 admission gate（G0-Device / G0-Automation）で決める。
- G600 live input route・G13入力・G600 Feature Report が成立するまで、ユーザーモード製品の成立性は Unverified。

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
