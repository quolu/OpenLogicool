# G600 F3 write-back で格納内容が 1 byte 右シフト（EXP-MIG-01 段2→段3 で検出）

- 日時: 2026-08-15（G0-Device-W 初回実機セッション）
- 状態: **調査中・追加 write 停止中**。restore 方針はオーナー裁定待ち
- 影響: G600 onboard profile 1（F3）の格納内容が意図しない値になっている。F0/F4/F5 は backup と一致（無傷）

## 事象

EXP-MIG-01 段2（F3 無変更 write-back）を実行した。

1. 開始条件: F0/F3〜F5 の実読が backup と全一致（write 前の device は健全）
2. 実読 F3（backup と一致）をそのまま SET_FEATURE → **同一 stream での直後 readback は backup と byte 一致**（段2は成立と表示された）
3. しかし段3の開始条件検査（新 process・新 open での実読）で F3 だけ不一致を検出。probe は設計どおり**何も書かずに停止**した

## 実測で確定した事実

- 現 F3 = backup F3 の **data 先頭（offset 1）に 0x00 が 1 byte 挿入され、末尾 1 byte が押し出された値**。この仮説は byte 単位で完全一致（`hyp == current` を機械検証済み）
- 3回の独立 re-read で現 F3 は安定（read 経路は健全）。F0/F4/F5 は backup と一致
- LGS（LCore）稼働中だが、シフトした F3 を LGS が書き戻す挙動は観測されていない（3 read で不変）
- HidSharp 2.1.0 の `WinHidStream.SetFeature` は buffer を無加工で `HidD_SetFeature` へ渡す（IL 逆コンパイルで確認）。host ライブラリ起因は棄却
- caps（FeatureReportByteLength）不一致なら `HidD_SetFeature` は失敗するはずだが成功している。Windows HID 層の長さ不整合も棄却
- probe の buffer 構築は [ReportID F3][data 153 bytes] の 154 bytes で正（コード実読で確認）

## 現時点の解釈（未確定）

消去法により、**G600 firmware が direct SET_FEATURE(F3) の payload を 1 byte ずれて格納する**挙動が最有力。LGS 純正の書込みは write 専用の F6 コマンド系（GET_FEATURE 不能・性質未解明）を経由している可能性があり、direct SET_FEATURE は Logitech の正規経路ではないのかもしれない。段2の「直後 readback 一致」は device/stack の echo か cache とみられ、**write 検証は独立 open の fresh read で行わなければならない**（重要な手順知見）。

## 教訓（確定分・手順へ反映すべきもの）

1. **readback は同一 stream の直後 read を証拠にしない**。新 process（最低でも新 open）の fresh read だけを検証に使う
2. Migration Safety Gate の設計（backup 必須・開始条件照合・stop-on-mismatch）は機能した。破壊は F3 一枚に封じ込められ、backup が完全である
3. R-02（G600 write で profile を壊す）は現実の risk だった。EXP-MIG-01 を全 write の前に置いた計画順序は正しかった

## restore の選択肢と経過

- **案A: 補償 write 1回 → 実施し反証された（2026-08-15、オーナー裁定で実施）**。`g600-f3-compensated-restore`（開始条件: 現 F3 が既知ずれ値と一致・F4/F5 が backup 一致）を実行。補償 write 自体は成功したが、fresh open の検証 read が backup と不一致。**2回の write で格納のずれ方が異なり（1回目: data 先頭へ 00 挿入 / 2回目: 別の位置ずれ）、「挿入は決定的」というモデルは反証された**。probe は設計どおり追加 write なしで停止。第2観測の実データ: `probe-output/g600-f3-compensated-restore-*.json`
- **案B: LGS 純正経路で restore（現在の唯一の推奨経路）**。LGS UI で G600 の onboard profile を再 push させる。実機手番が要る
- **案C: F6 コマンド系の解明**。direct SET_FEATURE は write 経路として反証済みのため、方式A/B の成立判定にはどのみち F6 系（LGS 正規経路）の理解が必要になった

## 方式判定への含意（G0-Device-W の材料）

direct SET_FEATURE による onboard write は、**書くたびに格納結果が異なる（非決定的または内部状態依存）**ことが2観測で確定した。したがって:

- 方式A（active slot 切替の F0 write）・方式B残存変種（中間 usage への書換え）は、**direct SET_FEATURE を write 手段とする限り成立しない**
- 成立の残り道は F6 コマンド系（LGS 正規経路）の解明だけ。これは protocol 調査（rag の公開実装・USB キャプチャ）を要する
- EXP-G600-04（154-byte 全量 write）は direct 経路では **不成立と判定**

## 二次障害と復旧（同日 19:43〜）

案B実行中、**LGS UI で onboard 側のライティング色を変更した瞬間にマウスが全操作不能**になった。観測事実:

- F0 が 2B→0A へ変化（LGS が device を onboard モード側へ切替えたとみられる）
- ずれた F3（profile 1）が active になり、DPI 表もずれているため実効 DPI が無効値＝カーソル停止、という機序で説明がつく
- **復旧**: LCore を停止 → 今朝 backup した `settings.json`／profiles を書き戻し → LCore 再起動（host 制御＝自動ゲーム検出へ復帰）。**マウス完全復旧をオーナーが確認**（19:5x）
- 復旧後の device: F4/F5 は依然無傷。F3 はずれたまま、F0 は 0B（意味未解読）。onboard 側の復元は未完

教訓の追加:

4. **onboard が壊れている状態で LGS を onboard モードへ切り替えてはならない**。壊れた profile が active になり、入力デバイスそのものを失う。onboard 復元の再試行時は、先にマウスキー等の代替入力を確保してから行う
5. LGS の host 設定（settings.json・profiles）の事前 backup は、device 側だけでなく **host 側の復旧経路としても機能した**（Migration Safety Gate の inventory #4 の価値の実証）

## 再試行（案B・オーナー裁定）と最終判定（同日 20:0x）

power cycle（USB 抜き挿し）で firmware runtime をリセット後、Claude が画面代行で LGS onboard 編集画面を操作した。結果:

- **LGS 純正経路の write もずれて格納される**ことを確認した。onboard 編集画面での操作後、無傷だった F5 が「1 byte 欠けた形」へ変化（F4 の read は backup と完全一致のままで read 経路の健全性は担保）。F3 は直らず
- ずれは direct SET_FEATURE 固有ではなく、**この device の feature write 全般が現在ずれて格納される状態**であり、power cycle でも解消しない
- これ以上の onboard 操作は全損リスクのため**即時撤退**。LGS を「自動ゲーム検出」（host 制御）へ戻して日常使用を確保した

最終 device 状態（`probe-output/mig01-backup-20260815/device-state-after-incident-20260815.json`）: F4 のみ backup 一致。F3・F5 は破損（backup は3面とも完全保持）。F0 は 08（意味未解読）。

**G0-Device-W 判定: 不通過（オーナー報告待ちの暫定）。onboard への write は経路を問わず全面凍結**。解除条件は「LGS 正規 write protocol（F6 コマンド系）の解明と、破損した F3/F5 の復元実証」。復元素材（F0〜F5 完全 backup・SHA-256 封入）は保持済み。

## 原因の訂正（公開実装調査後・2026-08-15 夜）

[rag/openlogicool/g600-write-protocol-2026-08-15.md](../../rag/openlogicool/g600-write-protocol-2026-08-15.md) の調査で、本 incident の初期結論「direct SET_FEATURE は経路として不成立」は**部分的に誤りと判明**した。

- 公開実装（libratbag / ecerulm / rom4ster の 3 件・すべて一次コード）は**全員 direct SET_FEATURE で F3/F4/F5 に 154-byte を直書き**しており、**我々の経路は正規だった**。F6 コマンド系を使う実装は存在しない。
- ずれの真因は「経路違い」ではなく **firmware の write が timing/handle 状態に敏感で単発 write では不安定**であること。ecerulm は「新 profile を載せるには 5〜10 回再送が要る（理由不明）」「open 後 2 秒 settle」「handle を再利用しない」を運用知として明記している。
- 我々は 1 回だけ write し、settle も retry もせず、handle も新規開閉していなかった（verify だけ fresh open）。**公開運用知を適用していなかった**のが不安定化の理由とみられる。

**F3/F5 復元の evidence-based path（未実行・裁定待ち）**: backup bytes を、毎回 fresh open → settle≥2s → SET_FEATURE → fresh open で readback → 一致まで最大 N 回再送。backup が完全なので後退リスクなし。ただし onboard write は現在凍結中で、本 path 実行はオーナー裁定を要する。

**方式判定への更新**: 方式A/B（onboard write を要する経路）は「不成立」ではなく「**settle+retry+fresh handle 前提なら公開実績あり**」へ格上げ。EXP-G600-03（F0 slot 切替）も同じ運用前提で再設計すれば成立余地がある。ただし F0 上位 nibble の誤操作は入力全喪失を招く（本 incident の二次障害と libratbag #1291 実機ログで実証済み）ため、slot 切替は 0..2 の正規値だけを扱う。

## 復旧成立（evidence-based restore・2026-08-15 夜・オーナー裁定）

公開運用知を焼き込んだ `g600-restore-retry`（fresh open → settle 2s → SET_FEATURE → 別 fresh open → settle 2s → readback → 一致まで最大 N 回再送）を実装し、F3/F5 を backup から復元した。

- **F3・F5 とも1回目の試行で backup と byte 完全一致**。3回目の独立 backup 読取でも F3/F4/F5 すべて backup 一致を確認（F0 は active-slot runtime 状態のため差分許容）。
- 前回ずれた原因は「単発 write・handle 再利用・settle なし」で確定的に説明がつく。fresh open + settle + 非再利用にしただけで一発成立した。
- **これにより Migration Safety Gate の restore 能力（DEV-010 の restore 実証部分）が初めて実機で成立**した。backup → 破損 → restore → byte 一致の一巡が通った。

**G0-Device-W の再評価**: onboard write は「凍結・不成立」から「**settle+retry+fresh handle 前提で成立（restore 実証済み）**」へ。ただし apply（意図的な内容変更）の実証と EXP-G600-03（F0 slot 切替）は未了。write 作法は本 probe の `g600-restore-retry` が確立した手順を踏襲する。

## apply 実証成立（onboard write 全周成立・2026-08-15 夜・オーナー裁定）

restore 作法を踏襲した apply 実証コマンド `g600-apply-verify` を実装し、実機で成立させた。restore（同一 bytes の書き戻し）だけでなく、**意図的に内容を変えた profile を byte 正確に書き込めるか**を検証したもの。

- 手順: clean 前提確認（F3/F4/F5 が backup 一致）→ F3 の LED RGB（offset 1–3）を XOR 反転した payload を evidence-based 作法で apply → fresh verify → apply の成否に関わらず backup を restore → fresh verify。鍵割当 byte は一切変更しない。
- 結果（`probe-output/g600-apply-verify-20260815-121407-438.json`）: **apply・restore とも attempt1 で byte 完全一致、exit 0**。最終 device 状態は backup と一致、日常使用（マウス）も正常をオーナー確認。
- 意味: **Migration Safety Gate DEV-010 の一巡（backup → 意図的改変 → byte 一致 → restore → byte 一致）が apply を含めて実証**された。onboard write は restore 片道でなく往復（書換→復元）で成立。Input Studio の onboard write 機能の中核前提が立った。

**G0-Device-W の再評価（更新）**: onboard write は「settle+retry+fresh handle 前提で成立（restore 実証済み）」から「**apply 往復まで実証済み**」へ。残る未了は EXP-G600-03（F0 slot 切替）と 0A UI 照合のみ。write 作法は `g600-restore-retry` / `g600-apply-verify` が共有する evidence-based 手順（fresh open・settle≥2s・handle 非再利用・fresh open で verify・一致まで再送）を正とする。

## 参照

- backup（無傷・SHA-256 封入済み）: `probe-output/mig01-backup-20260815/`
- apply 実証の実行記録: `probe-output/g600-apply-verify-20260815-121407-438.json`
- 段2成立表示の実行記録: `probe-output/g600-writeback-20260815-102124-305.json`
- 段3停止の実行記録: `probe-output/g600-led-apply-restore-20260815-102135-385.json`
- 手順定義: [migration-safety-gate.md](../migration-safety-gate.md)
