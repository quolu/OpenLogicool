# Capture backend probe 実測（Deliverable 0B / 2026-08-15）

計画 §6.9「第一候補は WGC で window 単位 capture、Desktop Duplication と可視 desktop 領域 capture は別 backend として probe」に対する実測。probe: [src/OpenLogicool.CaptureProbe](../../src/OpenLogicool.CaptureProbe)（read-only・backend 間 fallback なし）。

- 環境: FOX / Windows 11 Pro 26200 / .NET 10.0.400 / net10.0-windows10.0.22621.0
- 一次データ: `probe-output/capture-*.json` と各 `-frame{0,1}.png`
- 非黒判定は平均輝度（0–255）で行う。

## 結果

| backend | コマンド | 対象 | 解像度 | pixel format | 平均輝度 | 判定 |
|---|---|---|---|---|---|---|
| GDI BitBlt | `gdi` | 仮想スクリーン全体 | 5120×2880 | Format32bppArgb | 123.1 | **確認済み** |
| DXGI Desktop Duplication | `dup` | `\\.\DISPLAY1` | 5120×1440 | B8G8R8A8_UNorm | 164.5 | **確認済み** |
| Windows Graphics Capture (monitor) | `wgc-monitor` | ディスプレイ 1 | 5120×1440 | B8G8R8A8_UNorm | 164.4 | **確認済み** |
| Windows Graphics Capture (window) | `wgc-window <title>` | メモ帳 | 918×1021 | B8G8R8A8_UNorm | 241.4 | **確認済み** |

第一候補の WGC window capture が成立した。picker UI なしで `IGraphicsCaptureItemInterop.CreateForWindow` から item を作れることも確認済み（利用者選択 UI は製品要件 CAP-004 の別論点）。

## 失敗系の実測（CAP-002 の状態分類の材料）

**最小化 window の WGC capture は、item は作れるがフレームが来ない。**（`capture-wgc-window-20260815-151249.json`）

- `GraphicsCaptureItem` の生成は成功し、`IsIconic=true`、`Item.Size` が 150×23（最小化サムネイル相当）へ縮小。
- `Direct3D11CaptureFramePool.TryGetNextFrame()` が 5000ms 経過してもフレームを返さず、2 フレームとも `TimeoutException` として記録。fallback せず失敗のまま記録した。

含意: CAP-002 の「最小化」は *capture エラー* ではなく **item 有効・frame 供給停止＋サイズ急変** という組で検出する必要がある。サイズ変化は transform revision の更新契機（§6.9）にも当たるため、最小化検出を frame timeout だけに頼ると復帰時に古い locator を使う危険がある。

## 実装上の罠（次に触る人向け）

1. TFM を `net10.0-windows10.0.22621.0` にすると WinRT projection（`Windows.Graphics.Capture` 等）が追加 NuGet なしで使える。
2. `IGraphicsCaptureItemInterop` は CsWinRT 生成の `GraphicsCaptureItem.As<T>()` から取る。
3. `IDirect3DSurface` → `ID3D11Texture2D` は C# キャストでは `InvalidCastException`。`WinRT.CastExtensions.As<IDirect3DDxgiInterfaceAccess>()` が必要。
4. `Direct3D11CaptureFramePool.CreateFreeThreaded` を使うと DispatcherQueue 不要。console から扱える。
5. Vortice の出力列挙は `IDXGIAdapter.EnumOutputs(index, out output)`（`GetOutput` は無い）。`IDXGIOutput.Description` は `Dispose()` より前に読む。

## 未実施（Phase 0 の範囲外・必要時に追加 probe）

- borderless／fullscreen ゲーム window、multi-monitor 跨ぎ、DPI 変更中、HDR 有効時の挙動（CAP-005 の support matrix は Phase 4 の release gate 材料）
- 継続 capture（frame rate、drop、device lost、backend change）。本 probe は 2 フレーム取得までを確認する器である。
