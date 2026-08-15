# Data Flow Contract（Phase 0 draft / 2026-08-15）

計画 §6.12 が要求する data ごとの流路台帳。**Phase 4 前の確定**が計画上の期限で、Phase 0 では「項目の決定＝この表の骨格」までを成果とする。値のうち「既定」列は §6.12 の初期既定を写したもので、変更にはオーナー裁定が要る。

## 表

| data | 生成元 | 保存先 | 送信先 | retention | 削除経路 | 既定 |
|---|---|---|---|---|---|---|
| full-screen frame | Capture backend | メモリのみ | なし | プロセス寿命 | プロセス終了 | **永続保存 OFF** |
| evidence crop | Perception（Teach session で利用者が選択） | ローカル DB | app 単位で明示同意時のみ AI provider | 利用者が削除するまで | Teach session 単位で削除 UI | 保存は利用者選択 |
| OCR text | Perception | メモリ／Observation | 同上 | Observation の寿命 | Run 削除で連動 | **engineering log へは OFF** |
| window title | Capture／Profile | ローカル DB（ApplicationIdentity） | 送信しない（既定） | profile 寿命 | profile 削除 | — |
| process path | Profile | ローカル DB | 送信しない | profile 寿命 | profile 削除 | — |
| prompt / response | AI adapter | メモリ／Run journal（要約のみ） | AI provider（当然） | Run 保持期間 | Run 削除 | **本文の engineering log 記録 OFF** |
| Execution Journal | Playbook | ローカル DB | 送信しない | 利用者設定 | Run 単位削除 | — |
| Engineering Log | 全 Lane | ローカルファイル | 診断 bundle に含める場合のみ利用者操作 | ローテーション | ファイル削除 | telemetry **OFF** |
| device ID（VID/PID/path） | Device | ローカル DB | 送信しない | device 登録の寿命 | device 登録解除 | — |
| crash dump | OS／ホスト | ローカル | 送信しない | 利用者が削除するまで | ファイル削除 | **raw dump OFF** |
| diagnostic bundle | Diagnostics | 利用者が指定した場所 | 利用者が明示的に共有する場合のみ | 生成物として残る | ファイル削除 | 生成は利用者操作 |
| Knowledge Pack | import／export | ローカル | 利用者が export した場合のみ | pack の寿命 | pack 削除 | import 直後 Untrusted |

## 規則

- **cloud 送信は app 単位で明示同意するまで OFF**（§6.12）。同意は app ごと・mode ごとに独立し、1回の同意を他 app へ広げない。
- 送信先が「AI provider」となる data は、[ai-provider-evaluation-design.md](../ai-provider-evaluation-design.md) §4 の data policy チェックリストに回答がある provider に限る。
- **削除経路のない data を作らない。** 上表に削除経路を書けない data が出たら、その data を持たない設計へ戻す。
- 表にない data を新規に作る変更は、この表への追記を伴わなければ受け入れない（contract test の対象）。

## 未決定（Phase 4 の確定までに埋める）

- retention の既定値（日数）と利用者が変更できる範囲
- diagnostic bundle に含める data の既定集合（device ID を含めるか）
- evidence crop の保存形式と、画像に写り込む周辺情報の扱い（crop 範囲の最小化規則）
