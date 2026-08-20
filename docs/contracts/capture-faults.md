# Capture fault contract

`CaptureRead` は frame の無供給と capture fault を分ける。fault を伴わない `FrameUnavailable` は、WGC の変化駆動による静止であり fault でも連続性断絶でもない。

`CaptureFaultKind` は black、stale、drop、resize、device lost、backend change、occluded、minimized を別値で表す。`WgcFrameSource.PullDetailed()` は item size の変化と pool 再作成を `Resize`、item size が 0 の最小化を `Minimized` として返す。black／drop／device lost／occluded は、それぞれの検出者が `CaptureRead` の fault として gate へ渡す。通常の `Pull()` は既存の `IFrameSource` 互換結果を返す。

`CaptureContinuityGate` は明示 fault、backend change、transform revision change、stale frame を受けると自動入力を不許可にする。同一 source、backend、transform revision の新しい frame を `Recalibrate` した時だけ許可へ戻る。入力の送出や別 backend への fallback は行わない。
