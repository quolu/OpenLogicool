# t05 終端検証

判定日: 2026-08-26

## 実機

- learn: `game-interaction-foundation-purpose-run-20260826-005402-987.json`
- replay: `game-interaction-foundation-purpose-run-20260826-010722-497.json`
- replay直前ロビー: `game-interaction-foundation-observe-20260826-010630-030.json`
- 最終ロビー: `game-interaction-foundation-observe-20260826-010823-284.json`
- Nano再接続後direct smoke: `serial-hid-direct-smoke-20260825-233256-966.json`

## 試験

- focused: Foundry Local goal filter／類似OCR、known-first、route直接再生、正常edge再利用、失敗step差替え、1 anchor、Compare／learningを個別確認。
- 関連test: AI 43、Exploration 50、Host 212、Perception 31、Persistence 50、Probe 61、Architecture 8、Conformance 61。合計516件green。
- full regression: `dotnet test OpenLogicool.sln --no-restore`を最後に一回実行。22 test project・1192件green、失敗0、skip 0。
- build: Probeを警告0・error 0でbuild。
- `git diff --check`: error 0。

## 独立監査

- Peertableすずね #977: 初回AI探索と、ダイアログ無しロビーからのAI 0保存route再生を実画像・JSON・focused test本文で反証しPASS。
- Peertableこはる #979: learn／replay、同一DB、同一route／edge、SQLite保存内容、Nano-only、frame列をread-only突合し受入成立。
- 監査中に終了確認closeを目的達成としたfalse positive `005826-295`を検出し撤回。修正版 `010722-497`を再監査した。

## commit境界

- campaignの製品・test・docs・Lattice storeだけを対象とする。
- `LiveDiscoveryNanoActionSmoke.cs`、`LiveDiscoveryObserveSmoke.cs`、`LiveDiscoveryNanoActionSmokeTests.cs`と隣接未追跡testは別作業所有としてstageしない。
- 大量の既存未追跡probe出力とroom archiveは削除せず、commitへ含めない。

## 補助基盤修理

PeertableはWindows/Grok完了画面の`stop [hooks]`をbusyと誤認してDMを永久保留する欠陥をvendor別fileで修理した。0.8.7をnpm公開・公式global installし、既存Roomでidle→DM delivered→busy lampを実測した。Peertable mainは`ca2ff9c`までorigin/mainへpush済み。
