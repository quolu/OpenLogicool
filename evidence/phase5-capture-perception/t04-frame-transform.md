# t04 Frame transform 証跡

## 実施

- `FrameCoordinateTransform` を追加し、source→content→normalized→client→input を純粋変換として明示した。
- `FrameTransformTracker` は size、DPI、pixel format、letterbox content bounds の変化で revision を単調に進める。
- `WgcFrameSource` は frame ごとに frame 全体を content bounds として tracker に渡す。resize 後の最初の有効 frame は新しい transform revision を持つ。
- locator の有効性は同じ transform revision に pin される。fault 状態、backend change、stale の分類は t05 の所有であり、本 task は先取りしない。

## 根拠水準

- **確認済み**: Windows native の自前 window で resize→frame pool 再作成→拡大後 BGRA8 frame を実測し、transform revision が増えることを確認した。
- **確認済み**: pure test で DPI、pixel format（HDR format を含む表現）、letterbox content bounds の各変化が revision を進め、座標変換の全段と範囲外拒否を確認した。
- **未確認**: 複数 display 間の実移動、実 HDR display での frame format 変化、実ゲームの letterbox 検出。t02 の support matrix は未確認を Supported と表示しない。

## focused verification

| コマンド | 結果 |
| --- | --- |
| `dotnet test tests/OpenLogicool.Capture.Tests/OpenLogicool.Capture.Tests.csproj` | 5/5 green |
| `dotnet test tests/OpenLogicool.Capture.Tests/OpenLogicool.Capture.Tests.csproj --filter "Category=WindowsNative"` | 1/1 green |
| `dotnet build src/OpenLogicool.Host/OpenLogicool.Host.csproj` | green、警告 0／エラー 0 |
| `dotnet test tests/OpenLogicool.Conformance.Tests/OpenLogicool.Conformance.Tests.csproj` | 12/12 green |
