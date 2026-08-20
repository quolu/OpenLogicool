# Phase 5 campaign — Unverified close

- status: **completed**（2026-08-20 終端監査 accept。判定は [phase5-unverified-assessment.md](phase5-unverified-assessment.md)）
- 起票: 2026-08-20（オーナー指示「未確認事項の Lattice を作れ。その上で Phase 6 も作れ」）
- 統括: ベル（Grok 4.6）。実装 Terra×high（Codex）／監査 Grok 4.6×medium（実装と監査は別ベンダー。1位不通を次順位へ落とさない）
- 実行 TODO の正本: **Lattice plan `phase5-unverified`**
- 上位正本: [phase5-exit-assessment.md](phase5-exit-assessment.md) 残課題、[development-plan.md](development-plan.md) §Phase 5
- 先行: Phase 5 Exit 成立。本 campaign は Exit 判定外の未確認を4値で閉じるか、閉じられないものは未確認のまま残す

## 目的

Exit で「未確認として残した」面を、自走できるものは実測で確認済みへ上げ、人待ちは人待ちのまま追跡する。一般対応 claim にしない。

## 統括レーン判定と F/A/H

②受入が多段 ④証跡。Exit オーナー承認待ちは組まない。

- **F**: commit・push、H の start、t06 判定
- **A**: resident dispatch、PNG metric、カタログ照合
- **H**: 実 NIKKE 窓、実 display 条件（fullscreen／HDR 等）、G600 残置。席は H を取らない

## 円卓

入口は peertable room `OpenLogicool` だけ。setup.sh／parent-join はしない。pull run は本 plan 用に新規。

| 役割 | 配置 |
|---|---|
| 統括 | Grok 4.6（bell） |
| 実装 | Terra×high Codex |
| 監査 | Grok 4.6×medium |

待機中は `[次の行動]` 自己DMを出さない。preflight 失敗は席を立てず、別 model へ落とさない。

## 非目標

- Phase 6 の AI Teach
- G600 残置の実機確認（別手順 [g600-leftover-operation.md](g600-leftover-operation.md)）
- UI 保存と関連付けの導線（Phase 3 持ち越し）
- 失敗 backend への fallback
- fast path へ capture を載せる

## 受入条件

1. Host resident または CLI が `CaptureContinuityDispatch` を駆動する製品経路がある（test だけではない）
2. tracked PNG acceptance を metric runner が通し、3指標の数値が証跡に残る
3. 事前登録カタログと live frame を照合する（frame 自身の fingerprint 自己照合ではない）
4. NIKKE UniqueMatch は窓が無ければ未確認と明記して閉じる。無理に成立にしない
5. support matrix の live 未確認行は実測できた行だけ上げ、残りは Unverified のまま
6. 各 ToDo は focused green＋証跡＋着地。H は人待ちを未確認で残してよい
7. 通し試験は最終確認だけ

## 運用

H の t04／t05 は席が取らない。親がオーナー窓の用意後だけ start する。t06 は親。

## Lattice task 仕様（正本は store）

### t01-resident-dispatch-loop

Host の resident または CLI が `CaptureContinuityDispatch`（と resume 段）を駆動する。focused／native。FastPathPump には載せない。test 専用 wrapper 呼び出しを製品経路と数えない。

### t02-png-corpus-metrics

`FrozenMetricRunner` に tracked PNG（`fixtures/frames/` の acceptance）を通す。合成 BGRA8 だけの数値を PNG 数値と偽らない。3指標を証跡へ残す。training に acceptance を載せない。

### t03-catalog-live-match

事前登録した state／catalog と、自前 window の live WGC frame を照合する。live frame 自身の SHA から rule を作る自己照合は禁止。不一致は Known に丸めない。

### t04-nikke-live-unique

実 NIKKE 窓で UniqueMatch 再開の可否を実測する。席は取らない。窓が無ければ未確認のまま t06 へ残す。一般対応と書かない。

### t05-support-matrix-live

borderless／fullscreen／DPI／HDR／multi-monitor／遮蔽のうち、用意できる条件だけ live 実測して matrix 行を上げる。できない行は Unverified。席は取らない。

### t06-unverified-assessment

残課題の4値を `docs/phase5-unverified-assessment.md` に書く。親が閉じる。席は取らない。
