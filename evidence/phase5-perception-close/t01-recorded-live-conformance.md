# t01-recorded-live-conformance 証跡

## 作成物

- tracked PNG fixture `fixtures/frames/gamelab-main-menu-20260815.png` を BGRA8 bytes へ復元し、`CapturedFrame` として製品 `FixtureFrameRecognizer`／`LiveObservationSource.Observe` に渡す Windows native conformance test を追加した。
- 自前 WinForms window を WGC で capture し、取得した `CapturedFrame` も同じ recognizer と同じ `Observe` へ渡す。WGC の静止時無 frame を失敗扱いしないため、window は `Invalidate`／`Update` で再描画して frame を供給させる。
- `FakeObservationSource` は使わない。両経路で `Known`、frame source／backend／sequence／freshness、recognizer version、candidate と evidence region を確認する。

## 最終試験

1. `dotnet test tests/OpenLogicool.Capture.Tests/OpenLogicool.Capture.Tests.csproj --nologo --filter "FullyQualifiedName~RecordedLiveConformanceTests" --logger "console;verbosity=normal"`
   - 結果: Windows native focused test 1/1 passed。
2. `dotnet test tests/OpenLogicool.Capture.Tests/OpenLogicool.Capture.Tests.csproj --nologo --logger "console;verbosity=normal"`
   - 結果: Capture 関連 test 16/16 passed、0 failed。既存 WGC repaint native test も passed。
3. `git diff --check`
   - 結果: 出力なし。

## 非目標

実ゲーム一般への対応、recorded fixture からの学習、recognizer の閾値調整は実装していない。
