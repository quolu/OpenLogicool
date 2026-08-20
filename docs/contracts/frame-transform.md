# Frame transform contract

Frame の locator は必ず一つの `transformRevision` に属する。`FrameTransformTracker.IsCurrent` が false になった locator は無効であり、次の座標変換に再利用しない。

1. source pixel
2. letterbox を除いた content pixel
3. normalized（各軸 0..1）
4. target window client pixel
5. input pixel

`FrameTransformSignature` は width、height、pixel format、DPI、content bounds、capture 時点の monitor handle を比較する。WGC は各 frame で `MonitorFromWindow(..., MONITOR_DEFAULTTONEAREST)` の handle を渡すため、同一 scaling の別 display への移動でも、次に供給された frame で revision を増やす。resize、display/DPI 移動、HDR を含む format 変更、letterbox content bounds 変更のいずれでも revision を増やす。backend change と stale の状態分類は t05 の所有である。

`FrameCoordinateTransform` は content 範囲外の source 座標と 0..1 外の normalized 座標を拒否する。input への変換は client 座標に input origin を加えるだけであり、入力送信は行わない。

WGC source は capture 時点で content bounds を frame 全体として tracker へ渡す。letterbox の content bounds は、その検出者が tracker へ明示して revision を更新する。t04 は座標変換を定義するだけで、black／stale／backend change の fault 判定はしない。
