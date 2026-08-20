# t01 WGC Frame 証跡

## 実施

- `Windows.Graphics.Capture` の window capture を `WgcFrameSource` として製品モジュールへ移した。
- `Direct3D11CaptureFramePool.CreateFreeThreaded` と `IGraphicsCaptureItemInterop.CreateForWindow` を使い、D3D11 staging texture から BGRA8 pixel buffer を取得する。
- Frame は source ごとの連番、WGC の QPC monotonic time、wall clock、content size、DPI、pixel format、全体 crop、rotation を返す。WGC API が色空間を返さないため、色空間は推定せず `Unknown` とする。
- WGC は再描画駆動で、静止 window に frame が届かないのは正常である。`Pull()` はこの状態を `FrameUnavailable` として返し、別 backend へ fallback しない。

## 契約

- `CapturedFrame` は `ColorSpace`、`Rotation`、`Crop`、`Pixels` を持つ。詳細は `docs/contracts/captured-frame.md`。
- crop／transform revision／stale の意味付けは t04／t05 の所有範囲であり、t01 は全 content の座標を渡すだけに留めた。

## focused verification

| コマンド | 結果 |
| --- | --- |
| `dotnet test tests/OpenLogicool.Capture.Tests/OpenLogicool.Capture.Tests.csproj` | 2/2 green |
| `dotnet test tests/OpenLogicool.Capture.Tests/OpenLogicool.Capture.Tests.csproj --filter "Category=WindowsNative"` | 1/1 green（自前 window を再描画し、製品 `WgcFrameSource.Pull()` が BGRA8 frame を返すことを確認） |
| `dotnet build src/OpenLogicool.Host/OpenLogicool.Host.csproj --no-restore` | green、警告 0／エラー 0 |
| `dotnet test tests/OpenLogicool.Host.Tests/OpenLogicool.Host.Tests.csproj --filter "FullyQualifiedName!~HostWorkspaceEditorIntentsTests"` | 45/45 green |
| `dotnet test tests/OpenLogicool.Conformance.Tests/OpenLogicool.Conformance.Tests.csproj` | 12/12 green |

Host test 全49件のうち `HostWorkspaceEditorIntentsTests` 4件は、Lattice worktree の長いパスから SQLite の `e_sqlite3` をロードできない Windows の `0x800700CE` で失敗した。WGC／契約の assertion 失敗ではなく、Host build は green。問題を成功扱いにはしていない。

## 根拠水準

- Phase 0 の同一 Windows native probe で、WGC window の初回 frame と再描画時の後続 frame は確認済み（`docs/probes/wgc-frame-supply-2026-08-15.md`）。
- 本 task の製品 `WgcFrameSource` は、Windows native integration test で自前 window の再描画から `FrameAvailable` と BGRA8 buffer を確認済み。
