# FixtureFrameRecognizer contract（Phase 5 close / t03）

`FixtureFrameRecognizer` は本 campaign の recorded fixture と自前 window の状態を、登録済みの画素 SHA-256 で照合する製品 `IFrameRecognizer` である。ゲーム画面や未登録 source を一般に認識するものではない。

## 入力境界

- 登録規則は `sourceId`、幅、高さ、pixel format、BGRA8 画素 SHA-256 の完全一致で frame を識別する。
- 登録済み source のうち規則に一致しない画素は、校正済みで候補なしの結果を返す。`LiveObservationSource` はこれを `Unknown` にする。
- 登録されていない source、BGRA8 画素を持たない frame、空画素は契約外であり明示エラーにする。別 recognizer や capture backend への自動切替はしない。
- WGC の静止 window では新 frame が来ないことが正常である。frame が来ない間に recognizer は呼ばれず、これを capture fault や `Unknown` の合成根拠にしない。

## 状態規則

- 規則が未校正なら、候補が一つでも `LiveObservationSource` が `Unknown` にする。
- 校正済みの候補が一つなら `Known`、複数なら `Ambiguous`、候補なしなら `Unknown`。既存の4状態正規化以外の丸めはしない。
- 状態候補と evidence region は caller が fixture rule として明示登録する。認識器は Attempt、dispatch、InputEmitter を参照しない。

## 非目標

- 実ゲーム一般への対応、学習、閾値調整、未知画素からの推論は本 contract の範囲外である。
