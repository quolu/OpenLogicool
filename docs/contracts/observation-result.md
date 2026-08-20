# ObservationResult contract（Phase 5 / 2026-08-20）

Perception が recorded／live の CapturedFrame を状態候補へ変換した結果。意味 owner は Perception（Lane G）。どちらの入力でも同じ `LiveObservationSource` が同じ4状態へ正規化する。

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

- **Known への丸め禁止**: 複数候補の差が小さい・frame が古い・証拠領域が欠ける場合は `Ambiguous`/`Unknown` を返す（§6.9）。confidence は recognizer ごとの calibration dataset で定義し、未 calibration の recognizer は Known を出せない。candidate の confidence または evidence region が契約外なら、Perception は Observation を合成せず明示エラーにする。
- **Attempt を知らない**: ObservationResult は Attempt ID を持たない。Observation→Attempt の束縛は Playbook が commit する RunEvent だけで成立する（§6.7、契約4）。
- status enum の追加・意味変更は semantic breaking（§6.4）。`stateCandidates` の順序に意味を持たせない（confidence 降順表示は消費側の責務）。
- **安定観測窓**: `ObservationStabilityWindow` は同一 source／backend／transform revision の唯一 Known state が monotonic 時間で指定窓を満たす時だけ true を返す。Ambiguous、Unknown、Unavailable、state 変更、座標系変更、時刻逆行は窓をリセットする。操作前後の系列をどこで照合するかは Attempt を所有する Playbooks の責務である。

## 未決定

- evidenceRegions の形状表現（矩形のみ / polygon 許容）
- confidence calibration dataset の格納形式（Knowledge Pack `recognizers` 節との分担）
- Unavailable の診断カテゴリ一覧（CAP-002 の状態分類と同一 enum を共有するか）
- fixture の `_` で始まる property は provenance 注記として明示的に読み飛ばし、それ以外の未定義 field は deserialize error とする。
