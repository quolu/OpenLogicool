# Data Flow Contract（Phase 4 確定 / 2026-08-19）

計画 §6.12 と NFR-011、§11.3 の流路台帳。Phase 0 で項目骨格を決め、本書で Phase 4 着手に必要な値を閉じる。表にない data を新規に持つ変更は、この表への追記なしでは受け入れない。

確度: 表の流路は計画の初期既定を写した**確認済み**。retention 日数は本確定の**強い推定**（運用で変えるときは本書を先に直す）。実画面 crop の画素規則は Phase 5 まで**未確認**（形式と最小化方針だけ先に固定）。

## 表

| data | 生成元 | 保存先 | 送信先 | retention | 削除経路 | 既定 |
|---|---|---|---|---|---|---|
| full-screen frame | Capture backend | メモリのみ | なし | プロセス寿命 | プロセス終了 | **永続保存 OFF** |
| evidence crop | Perception（Teach で利用者が選択） | ローカル画像＋DB 参照 | app 単位で明示同意時のみ AI provider | 利用者が削除するまで | Teach session 単位の削除 UI | 保存は利用者選択。形式は PNG。範囲は EvidenceRegion のみ |
| OCR text | Perception | メモリ／Observation | 同上 | Observation の寿命 | Run 削除で連動 | **engineering log へは OFF** |
| window title | Foreground tracker | ローカル DB（関連付け・診断） | 送信しない | 関連付けの寿命 | 関連付け削除 | 診断 bundle 既定除外 |
| process path | Foreground tracker | ローカル DB | 送信しない | 関連付けの寿命 | 関連付け削除 | 診断 bundle 既定除外 |
| prompt / response | AI adapter | メモリ。journal は要約だけ | AI provider（本文は provider へ） | Run 保持期間 | Run 削除 | **本文の engineering log 記録 OFF** |
| Execution Journal | Playbook runtime | ローカル DB | 送信しない | 既定 90 日。利用者は 1〜365 日または「削除するまで」 | Run 単位削除。期限切れは preview してから削除 | 本文は bundle 既定除外 |
| Engineering Log | 全 Lane | ローカルファイル | 利用者が bundle に含めたときだけ | 14 日ローテーション | ファイル削除 | telemetry **OFF**。OCR／prompt 本文を書かない |
| device ID（VID/PID） | Device | ローカル DB | 送信しない | device 利用の寿命 | device 登録解除 | bundle に含めてよい |
| device path | Device | メモリ／必要なら DB | 送信しない | プロセス寿命または登録寿命 | 登録解除 | bundle 既定除外（user 名を含みうる） |
| crash dump | OS／ホスト | ローカル | 送信しない | 利用者が削除するまで | ファイル削除 | **raw dump OFF**（作らない） |
| diagnostic bundle | Diagnostics | 利用者が指定した場所 | 利用者が明示共有するときだけ | 生成物として残る | ファイル削除 | 生成は利用者操作。secret redaction 失敗は共有不可 |
| Knowledge Pack | import／export | ローカル | 利用者が export したときだけ | pack の寿命 | pack 削除 | import 直後 Untrusted |
| GameLab oracle / fake Observation | GameLab／test | Execution Journal と同じ | 送信しない | journal と同じ | journal と同じ | Phase 4 の「現在 state」根拠。実画面ではない |

## 規則

- **cloud 送信は app 単位で明示同意するまで OFF**。同意は app ごと・mode ごとに独立し、1回の同意を他 app へ広げない。
- 送信先が「AI provider」となる data は、provider 評価の data policy に回答があるものに限る。
- **削除経路のない data を作らない。** 書けない data は持たない。
- 削除は対象を preview してから行う（SQLite、image、cache、temp、upload queue、bundle、backup）。preview なしの一括破棄を製品機能にしない。
- secret は Windows Credential Manager または CurrentUser scope。export 対象外。
- Phase 4 は実画面を使わない。frame／OCR／crop の永続経路をこの Phase で実装しない。journal 実装（t03）は本表の journal 行に従う。

## 診断 bundle の既定集合

含める: build、schema version、OS、device 種別、VID/PID、firmware（既にローカルにある値だけ）、capture backend 名、error category、correlation ID、wall／monotonic time。

含めない: frame、evidence crop、OCR 本文、prompt／response 本文、journal 本文、crash dump、secret、process path、window title、device path。

device ID は VID/PID だけを含め、path は含めない。

## evidence crop（Phase 5 実装時の拘束。今は実装しない）

- 形式: PNG
- 範囲: Observation の EvidenceRegion。余白を足して周辺 UI を写さない
- 保存は Teach で利用者が選んだときだけ
- 実画面が写るため、bundle 既定と cloud 既定は OFF のまま

## Phase 0 から閉じた未決定

| 項目 | 確定 |
|---|---|
| retention 既定と変更範囲 | journal 90 日（1〜365 日または削除するまで）。engineering log 14 日。その他は表どおり |
| bundle 既定集合 | 上記「診断 bundle」。VID/PID は含め、path は含めない |
| crop 形式と最小化 | PNG、EvidenceRegion のみ。画素の実測は Phase 5 |
