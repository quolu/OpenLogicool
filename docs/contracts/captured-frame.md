# CapturedFrame contract

Capture が Perception へ渡す一枚の frame。意味 owner は Capture（Phase 5 t01）。

- `sourceId` と `backend` は取得経路を表す。
- `sequence` は source ごとに単調増加する。
- `monotonicMs` は WGC compositor の QPC 時刻、`wallClockUtc` は取得時刻である。
- `width`、`height`、`pixelFormat`、`dpiX`、`dpiY`、`rotation`、`crop` は pixel buffer の座標系を表す。
- WGC first backend は BGRA8 buffer を `pixels` に保持する。`stride` は buffer の一行の byte 数である。
- WGC API は色空間を返さないため、t01 では `colorSpace=Unknown` とする。HDR 等を推定しない。
- t01 は crop を行わず、`crop` は content 全体を表す。transform revision、resize、stale の意味付けは t04／t05 が所有する。
- frame 非到着は静止画面では正常であり、`FrameUnavailable` を capture failure と解釈しない。
