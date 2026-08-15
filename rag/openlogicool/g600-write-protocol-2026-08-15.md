# G600 onboard profile write protocol — 公開実装調査（2026-08-15）

- 取得日: 2026-08-15
- 手段: Grok 4.6×high の web + 公開一次コード調査（read-only）
- 確度: 高（一次資料コード 3 実装 + descriptor ダンプ + libratbag issue の実機ログ）
- 動機: [docs/incidents/2026-08-15-g600-f3-writeback-shift.md](../../docs/incidents/2026-08-15-g600-f3-writeback-shift.md) の feature write ずれ事象の原因究明と、正規 write 経路の特定

## 結論（先に要旨）

1. **公開世界が実証している write 経路は「report 0xF3/F4/F5 へ 154-byte（ID+153）を 1 回の SET_FEATURE で書く」ただ一つ**。active slot 切替は 0xF0 の 4-byte SET_FEATURE。**F6 コマンド系を使う実装は存在しない**——我々の direct SET_FEATURE は、公開実装と同じ正規経路だった。
2. **「LGS は F6 コマンド経由で書く」という当プロジェクトの仮説は、公開一次資料では裏付けられない**。F6 は descriptor 上 154-byte profile ではなく 8-byte の Input/Feature 枠であり、154-byte GET は libratbag でも `-32 EPIPE` で失敗する（我々の GET_FEATURE 失敗と一致）。
3. **incident の結論を訂正すべき点**: ずれの原因は「経路が違う」ではなく、**firmware の書込みが timing/handle 依存で不安定**であること。公開実装は「数回再送しないと載らない」「handle を再利用しない」「open 後に待つ」を運用知として持っている。

## 公開 write 実装（onboard profile を実際に書く 3 件・すべて一次コード）

| 実装 | 対象 | write 経路 | F6 |
|---|---|---|---|
| libratbag `logitech_g600` driver | Linux | `HIDIOCSFEATURE` で 0xF3/F4/F5 に 154-byte 一発。slot 切替は 0xF0 の `{0xF0, 0x80\|(index<<4), 0,0}` | 不使用 |
| ecerulm/python-hidapi-logitech-g600 | macOS/hidapi | `hidapi.send_feature_report()` で F3/F4/F5、154-byte | 不使用 |
| rom4ster/g600Control | Windows/hidapi | Usage Page 0xFF80 / Usage 0x80 の TLC を開き `hid_send_feature_report` で 0xF3 固定 154-byte | 不使用 |

出典:
- https://github.com/libratbag/libratbag/blob/master/src/driver-logitech-g600.c （`LOGITECH_G600_REPORT_ID_PROFILE_0/1/2`=0xF3/F4/F5、`REPORT_SIZE_PROFILE`=154、`logitech_g600_write_profile()`、`logitech_g600_set_active_profile()`）
- https://github.com/libratbag/libratbag/blob/master/data/devices/logitech-g600.device （`usb:046d:c24a` / `Driver=logitech_g600`）
- https://github.com/ecerulm/python-hidapi-logitech-g600
- https://github.com/rom4ster/g600Control

write しない実装（列挙・除外）: OpenRGB（Issue #920 のみ・未実装）、Solaar（HID++ 非対応で close）、mafik/tingwai/Dessix の各 remapper（入力フックのみ）、Piper（libratbag GUI）。

## report 0xF6 の実体（descriptor 一次）

OpenRGB #920 に貼られた G600 の生 HID report descriptor（Usage Page 0xFF80）より:

| Report ID | 種別 | データ長 | 合計 |
|---|---|---|---|
| 0x80 | Input | 5 | 6 |
| 0xF0 | Feature | 3 | 4 |
| 0xF1 | Feature | 7 | 8 |
| 0xF2 | Feature | 4 | 5 |
| 0xF3/F4/F5 | Feature | 153 | 154 |
| **0xF6** | **Input と Feature の両方** | **7** | **8** |
| 0xF7 | Input | 31 | 32 |

- F6 は **8-byte のコマンド/通知枠**であり 154-byte profile ではない。154-byte GET は descriptor 違反で `-32`（libratbag #1291 の実機ログ、我々の `GetFeature failed` と一致）。
- **F6 の 7-byte payload の語彙（opcode/unlock/commit 等）は、公開コード・issue・逆解析記事のいずれにも存在しない。** LGS が F6 を write に使うか否かは公開一次資料では未確認。
- 出典: https://gitlab.com/CalcProgrammer1/OpenRGB/-/issues/920#note_474956399 、 https://github.com/libratbag/libratbag/issues/1291

## incident を説明する運用知（重要・一次/二次）

- **H1 再送が要る（ecerulm README・二次だが作者運用メモ）**: 「新 profile を載せるにはスクリプトを 5〜10 回実行する必要があるかもしれない。理由は不明」。CLI に `--settle_seconds`（default 2、open 直後は前の write を適用中の可能性）と `--reuse_connection`（default false、「一部の環境/firmware は再利用 handle を拒否する」）。
- **H4 Windows の Feature 長 padding（一次・descriptor 整合）**: Windows HID は同一 TLC の `FeatureReportByteLength` を最大（154）へ揃える。F0/F1/F2 を 154-byte で読めて先頭だけ有効なのはこれ。F6 を 154-byte で GET/SET するのは descriptor 違反。
- **H5 F0 上位 nibble は危険（libratbag #1291・一次コード+実機ログ）**: F0 第2 byte は bitfield で profile index を持つ。無効 index 3 を立てるとマウスが壊れたように見え、`F0 80`（profile 0）で復旧する実機ログがある。我々の incident で F0 が 2B→0A→0B→08 と動き、onboard 切替でマウスが死んだのと同系統。
- checksum・page 分割・commit report は **3 実装のいずれにも存在しない**（154-byte を 1 発）。

## 我々の incident への含意（結論の訂正）

- direct SET_FEATURE(F3) は **正規経路だった**。「経路が違うから凍結」は誤り。正しくは **write が firmware の timing/handle 状態に敏感で、単発 write では不安定**。
- 我々は 1 回だけ write し settle も retry もしていなかった。公開運用知（settle_seconds≥2・handle 再利用しない・5〜10 回再送・fresh open で検証）を適用していない。
- **F3/F5 復元の evidence-based path**: backup の F3/F5 bytes を、(1) 毎回 fresh open (2) open 後 settle≥2s (3) SET_FEATURE (4) fresh open で readback (5) 一致まで最大 N 回再送（不一致でも backup があるので後退なし）。これは公開実装の運用そのもの。
- ただし device write は現在オーナー裁定で凍結中。本 path の実行可否は別途裁定を要する。

## HID++ 対応

G600 は HID++ 1.0/2.0 ではなく専用 vendor protocol（Solaar メンテナが descriptor 実見の上 close、descriptor に 0x10/0x11 short/long report が無い、OpenRGB 開発者も同判断）。libratbag の G600 driver は hidpp20 を呼ばない独立 driver。
