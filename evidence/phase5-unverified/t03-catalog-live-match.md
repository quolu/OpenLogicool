# t03-catalog-live-match

## 実施

- self WGC window を装飾なし・160×90・単色へ固定し、capture 前に Navy の BGRA8 SHA-256 を持つ `self-window` catalog rule を登録した。
- live frame を取得してから fingerprint から rule を生成していた既存の自己照合を撤去した。
- 同じ source の Teal live frame は catalog に不一致であり、`LiveObservationSource` が `Unknown` のままにすることを同一 native test で確認した。
- `FixtureFrameRecognizer` contract に、live WGC rule は capture 前に登録し、不一致を `Known` へ丸めないことを記録した。

## 検証

一時 artifacts path と source root を指定して次を実行した。

```powershell
dotnet test .\tests\OpenLogicool.Capture.Tests\OpenLogicool.Capture.Tests.csproj --no-restore --artifacts-path C:\Users\kite_\AppData\Local\Temp\openlogicool-t03-artifacts-54228 --filter "FullyQualifiedName~RecordedLiveConformanceTests" --logger "console;verbosity=normal"
```

結果: Windows native focused test 1 passed / 0 failed。

- recorded PNG と Navy live WGC frame は、事前登録 rule を通る同じ製品 observation 経路で `Known`。
- Teal live WGC frame は同じ catalog に不一致で `Unknown`、candidate は空。

## 変更ファイル

- `tests/OpenLogicool.Capture.Tests/RecordedLiveConformanceTests.cs`
- `docs/contracts/fixture-frame-recognizer.md`
- `evidence/phase5-unverified/t03-catalog-live-match.md`
