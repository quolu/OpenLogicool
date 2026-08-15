# Migration Safety Gate 手順定義（Deliverable 0B / 2026-08-15）

計画 [§11.1](development-plan.md) の flow（inventory → dry-run → 利用者確認 → apply → readback → restore test）を、実機で実行可能な手順へ具体化する。本書は **Phase 0 の「手順定義」deliverable** であり、apply・restore の実証は G0-Device-W（EXP-MIG-01）で行う。Phase 0 では device write を一切行わない。

## 1. Inventory（backup 対象の全量）

| # | 対象 | 取得手段 | 現在の成立状況 |
|---|---|---|---|
| 1 | LGS version・mode・process/service 一覧 | LGS UI 表示＋`Get-Process`/`Get-Service`（LCore 等） | 手段確立済み（9.04.49 稼働を実測済み） |
| 2 | Logitech driver・G HUB・Dynamic Lighting の有無と版 | PnP 列挙＋インストール済みプログラム一覧 | 手段確立済み |
| 3 | G13/G600 descriptor・firmware release・device path | probe `enumerate`（実装済み） | **取得実証済み**（`probe-output/enumerate-2026-08-15.json`） |
| 4 | LGS host profile・macro・app association | `%LOCALAPPDATA%\Logitech\Logitech Gaming Software\` 配下の丸ごと複製（`profiles\{GUID}.xml` が profile 本体、`settings.json`・`ghubDevices.json` も同梱） | **確認済み**（実パスと構造を実測。[lgs-parity-inventory-2026-08-15.md](lgs-parity-inventory-2026-08-15.md)） |
| 5 | G600 F0〜F5 完全 Feature Report | probe `g600-backup`（実装済み） | **取得実証済み**（`probe-output/g600-backup-2026-08-15.json`） |
| 6 | 各 backup の SHA-256・取得日時 UTC・機体名 | backup 生成時に manifest として同梱 | 未実装（機械作業） |

F6 は GET_FEATURE が失敗するため backup に含められない（[probes/g600-profile-decode-2026-08-15.md](probes/g600-profile-decode-2026-08-15.md)：write 専用コマンド系の仮説・未確認）。したがって **F6 への write は、その性質が確定するまで全 Phase で禁止**とする。

## 2. Backup 手順

1. LGS を稼働状態のまま inventory #1〜#3 を取得する（デバイス状態を変えない）。
2. inventory #4 の LGS プロファイルファイルを複製する（LGS 停止は不要。ファイルロック時のみ LGS 停止→複製→再起動とし、その事実を manifest に記録する）。
3. probe `g600-backup` で F0〜F5 を取得する。
4. **二度読み一致で readback を確認する**: 手順3を2回実行し、F0 を除く F3〜F5 が byte 単位で一致すること（F0 は active 状態を含むため差分許容とし、差分があれば内容を記録する）。
5. 全成果物の SHA-256 と取得日時を manifest に書き、1つのディレクトリへ封入する。

## 3. Restore 手順（定義のみ・実証は EXP-MIG-01）

- restore の単位は **154-byte 全量の SET_FEATURE write-back** とする。フィールド単位の部分書込はしない。未確認 offset（6–11, 18–23, 24, 25–30）は解釈せず backup の bytes をそのまま保持する（read-modify-write）。
- LGS host profile の restore は、複製したファイルの書き戻し＋LGS 再起動とする。
- 自動 restore が不能な項目は、成功と表示せず「手動再設定＋元環境へ復帰」の手順を利用者へ提示する（計画 §11.1 の正直表示原則）。

## 4. EXP-MIG-01 の実証手順と受入条件

実証は次の順で行い、**各段が失敗したら停止して原因を記録する。別経路への fallback はしない**。

1. **backup**: §2 を完走し、二度読み一致まで成立。
2. **無変更 write-back**: F3 を読み、同一 bytes を SET_FEATURE で書き戻し、再 read が backup と byte 一致する。これが最初の device write であり、**この成立をもって profile レイアウト（[probes/g600-profile-decode-2026-08-15.md](probes/g600-profile-decode-2026-08-15.md)）の各フィールドを「確認済み」へ昇格できる**。
3. **最小変更 apply → restore**: 単一フィールド（LED RGB を推奨——入力系へ影響しない）だけ変えた 154 bytes を書き、readback で変更を確認後、backup から restore して byte 一致まで戻す。
4. **利用環境の無害確認**: restore 後に G9〜G20 の入力配送が実測どおり（[probes/g600-input-map-2026-08-15.md](probes/g600-input-map-2026-08-15.md)）であることを focused に確認する。

受入: 1〜4 全成立。ひとつでも欠けたら G0-Device-W を通過させず、次の focused experiment だけを Ready にする（計画 No-Go 準拠）。

## 5. 禁止事項（このゲートの範囲でも変わらないもの）

- LGS profile の削除を既定にしない。
- F6 への write（性質未確定のため）。
- backup なしの write、readback なしの apply、restore test なしの本適用。
