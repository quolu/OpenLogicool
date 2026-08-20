# Phase 5 Unverified close assessment

- 作成: 2026-08-20（統括 bell-grok46）
- 上位正本: [phase5-unverified-campaign-plan.md](phase5-unverified-campaign-plan.md)、[phase5-exit-assessment.md](phase5-exit-assessment.md)
- 工程正本: Lattice plan `phase5-unverified`
- 根拠4値: 確認済み／強い推定／未確認／非対応

## 判定

A（t01–t03）は製品経路と focused native で閉じた。H（t04/t05）は人待ちの実窓・実 display が無いので未確認のまま残す。一般対応にしない。

## A

### t01-resident-dispatch-loop — 確認済み

Host CLI が `CaptureContinuityDispatch`（resume 段含む）を駆動する。FastPathPump には載せない。実装 head `66c0539` は origin/main の祖先。focused Host `CaptureContinuityDispatchTests` 5 green。

### t02-png-corpus-metrics — 確認済み

tracked PNG acceptance を `FrozenMetricRunner` に通し、合成 BGRA8 だけの数値を PNG と偽らない。実装 head `2eb7565` は origin/main の祖先。focused `FrozenMetricRunnerTests` 3 green。

### t03-catalog-live-match — 確認済み

事前登録 catalog と自前 window の live WGC frame を照合する。live frame 自身の SHA から rule を作る自己照合は撤去。不一致は Unknown のまま。実装 head `6fad5f8` は origin/main の祖先。focused `RecordedLiveConformanceTests` 1 green。

## H（席は取らない）

### t04-nikke-live-unique — 未確認

実 NIKKE 窓がこのクローズ時点で用意されていない。UniqueMatch 再開の live 成否は未測。未確認のまま残す。一般対応と書かない。

### t05-support-matrix-live — 未確認

borderless／fullscreen／DPI／HDR／multi-monitor／遮蔽の追加 live 行は、このクローズ時点で用意した条件が無い。matrix の Unverified 行は Unverified のまま。

## 対象外

G600 残置の実機確認は [g600-leftover-operation.md](g600-leftover-operation.md)。本 campaign では扱わない。

## 閉じ方

A は確認済み。H は未確認。失敗経路を別方式へ fallback して成功扱いはしていない。
