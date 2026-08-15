# G600 onboard profile (0xF3–0xF5) 154-byte 解読（Deliverable 0B / 2026-08-15 実測）

- 一次データ: `probe-output/g600-backup-2026-08-15.json`（firmware release 7702、GET_FEATURE read-only）
- 解読手法: rag の libratbag 系プロトコル知識をレイアウト仮説とし、実測3プロファイルの内部整合で検証
- 根拠水準: **レイアウト全体は「強い推定」**。read-only 実測のみで write→readback の往復検証を行っていないため「確認済み」とはしない。ただし下記の独立整合が3本成立しており、誤読の余地は小さい:
  1. F4 の side 12ボタン = テンキー usage（Kp1〜Kp0, Kp-, Kp+）が、live input 実測で観測した LGS 仮想キーボードのテンキー配送と一致
  2. F5 の DPI 列 = 400/1200/2000/3200（×50 encoding）が G600 の既知工場既定と一致
  3. LED RGB（F4=白 FFFFFF、F5=緑 00FF00）と G-Shift 層 LED が構造位置どおりに出現

## レイアウト（154 bytes、offset は Report ID 含む）

| offset | 長さ | 内容 | 根拠 |
|---|---|---|---|
| 0 | 1 | Report ID (0xF3/F4/F5) | 確認済み |
| 1–3 | 3 | LED RGB | 強い推定 |
| 4 | 1 | LED effect（00=固定, 01=breathe, 02=cycle と推定） | 強い推定 |
| 5 | 1 | LED effect rate | 強い推定 |
| 6–11 | 6 | 全プロファイルで 00。意味未特定 | 未確認 |
| 12 | 1 | DPI shift 値（×50、00=無効） | 強い推定 |
| 13 | 1 | DPI default index（1 始まり） | 強い推定 |
| 14–17 | 4 | DPI table 4 slot（×50、00=slot 無効） | 強い推定 |
| 18–23 | 6 | 全プロファイルで 00。意味未特定 | 未確認 |
| 24 | 1 | 全プロファイルで 02。report rate 候補 | 未確認 |
| 25–30 | 6 | 全プロファイルで 00。意味未特定 | 未確認 |
| 31–90 | 60 | 通常層 button map G1–G20 × 3 bytes | 強い推定 |
| 91–93 | 3 | G-Shift 層 LED RGB | 強い推定 |
| 94–153 | 60 | G-Shift 層 button map G1–G20 × 3 bytes | 強い推定 |

### button 1項目 = 3 bytes（mouseCode, modifiers, hidKey）

- `mouseCode != 0`: マウス／デバイス機能。実測で出現した code: 01=左, 02=右, 03=中, 04=Back, 05=Forward, 11=DPI Up, 12=DPI Down, 13=DPI Cycle, 14=Profile Cycle, 15=DPI Shift, 16=DPI Default, 17=G-Shift
- `mouseCode == 0`: キーボード。modifiers は HID modifier bitmask（01=LCtrl, 02=LShift, …）、hidKey は HID Usage（Keyboard page）
- `00 00 00`: 割当なし

## 3プロファイルの実体（要旨）

| | F3 (profile 1) | F4 (profile 2) | F5 (profile 3) |
|---|---|---|---|
| LED | 黒 / cycle | 白 / 固定 | 緑 / breathe |
| DPI | 1200 のみ | 1200 のみ | 400/1200/2000/3200, shift 400 |
| G1–G5 | 標準マウス5機能 | 同左 | 同左 |
| G6（薬指） | G-Shift | G-Shift | DPI Shift |
| G7 | Kbd Shift+B | Kbd Shift+B | DPI Cycle |
| G8 | Profile Cycle | Profile Cycle | Profile Cycle |
| G9–G20（side 12） | Kbd 1234567890-= | Kbd テンキー Kp1..Kp0, Kp-, Kp+ | 数字キー＋DPI 操作混在 |
| G-Shift 層 | side に LCtrl+同キー | side に LCtrl+同キー | 全 slot 無効（00） |

詳細 dump は一次データから再現可能（decode スクリプトはレイアウト表のとおり機械的）。

## 中段4番目ボタンの判定（input-map の未解決事項）

**仮説 (b)「onboard profile がマウス機能へ割当済みのため raw report で機能側 bit として出る」は棄却。**
3プロファイルすべてで side 12ボタン（G9–G20）は全数キーボード割当であり、マウス機能へ割り当てられた side ボタンは存在しない。残る仮説は (a) スイッチ故障 (c) 押し漏れ。なお過去の再試行セッションは下段を対象としており、**中段4番目そのものを狙った単独再試行は未実施**——10秒の focused 再試行で (c) を判定できる。

## F6 read 失敗の机上所見

- F6 は HID descriptor 上 154-byte feature として宣言されているが GET_FEATURE が IOException で失敗（実測）。
- F0（active profile 系）・F3–F5（保存 profile）で既知プロトコルの read 面は完結しており、rag の一次資料にも F6 を read する記述はない。**write 専用のコマンド系 report という仮説（未確認）**。read-only Phase 0 では検証手段がなく、これ以上は Migration Safety Gate 通過後の write 実験の論点として持ち越す。

## Migration Safety Gate への含意

- backup は F0–F5 の生 bytes 全量保存で足りる（本 fixture がその形式）。restore は 154-byte 全量の SET_FEATURE write-back を単位とし、**未確認 offset（6–11, 18–23, 24, 25–30）は解釈せず bytes ごと保持**する read-modify-write を必須とする。
- write 実験の最初の受入は「F3 を読み、無変更で write-back し、再 read が一致する」こと。これが成立して初めてレイアウトの各フィールドを「確認済み」へ昇格できる。
