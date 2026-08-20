# recorded／live Observation conformance（Phase 5 close / t01）

recorded fixture の PNG 画素と自前 WinForms window の live WGC frame は、どちらも `CapturedFrame` として製品 `LiveObservationSource.Observe` へ渡す。recorded 専用の fake queue や別の Observation 実装は使わない。

- recorded は tracked fixture PNG を BGRA8 bytes に復元し、`sourceId=fixture:gamelab-main-menu` の `CapturedFrame` にする。
- live は WGC の `WgcFrameSource` が返す `CapturedFrame` をそのまま渡す。静止 window で新 frame が来ないことは正常なので、native conformance test は自前 window を再描画して frame を得る。
- 両入力は `FixtureFrameRecognizer` の許可済み rule に登録して `LiveObservationSource` の4状態正規化を共有する。test は各 frame の source／backend／sequence／freshness、recognizer version、candidate と evidence region を含む `Known` Observation を確認する。
- 実ゲーム一般への対応や、recorded fixture を使う学習・閾値調整はこの conformance の範囲外である。
