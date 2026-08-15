# ObservationResult contract draft（Phase 0 / 2026-08-15）

Perception が CapturedFrame を状態候補へ変換した結果。意味 owner は Perception（Lane G）。本書は Phase 0 の draft であり、G0-Automation gate「実 frame と GameLab prototype の fixture 案で表現可能」で判定する。実装は decision（計画 §16）後。

fixture: [fixtures/contracts/observation-result.sample.json](../../fixtures/contracts/observation-result.sample.json)

## フィールド

| field | 型 | 意味 |
|---|---|---|
| schemaVersion | string (semver) | 本 contract の版 |
| observationId | string (stable ID) | 一意。RunEvent からの参照キー |
| frame | object | 参照 CapturedFrame の要約: source, backend, sequence, monotonicTime, wallClockUtc, transformRevision |
| status | enum | `Known` / `Ambiguous` / `Unknown` / `Unavailable` |
| stateCandidates | array | 候補ごとに stateId（Knowledge Pack `states` の stable state ID）, confidence [0,1], evidenceRegions |
| evidenceRegions | array | normalized coordinates（Frame contract の座標変換順 §6.9 に従う）と根拠種別（recognizer 名） |
| recognizerVersion | string | confidence の calibration が紐づく recognizer 版 |
| freshnessMs | number | frame 取得から本結果生成までの経過（monotonic 基準） |
| unavailableReason | string? | status=Unavailable の時だけ。capture black / stale / backend loss 等の診断カテゴリ |

## 意味規則（計画からの継承）

- **Known への丸め禁止**: 複数候補の差が小さい・frame が古い・証拠領域が欠ける場合は `Ambiguous`/`Unknown` を返す（§6.9）。confidence は recognizer ごとの calibration dataset で定義し、未 calibration の recognizer は Known を出せない。
- **Attempt を知らない**: ObservationResult は Attempt ID を持たない。Observation→Attempt の束縛は Playbook が commit する RunEvent だけで成立する（§6.7、契約4）。
- status enum の追加・意味変更は semantic breaking（§6.4）。`stateCandidates` の順序に意味を持たせない（confidence 降順表示は消費側の責務）。

## 未決定（decision まで interface と fixture のみ）

- evidenceRegions の形状表現（矩形のみ / polygon 許容）
- confidence calibration dataset の格納形式（Knowledge Pack `recognizers` 節との分担）
- Unavailable の診断カテゴリ一覧（CAP-002 の状態分類と同一 enum を共有するか）
