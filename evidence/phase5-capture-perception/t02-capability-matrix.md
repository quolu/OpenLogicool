# t02-capability-matrix — CAP-004／005

## 実施

- backend、target、条件ごとの根拠4値（`Confirmed`／`StrongInference`／`Unverified`／`Unsupported`）と、製品 route の可否（`Available`／`ProbedOnly`／`Unavailable`）を分離した。
- `CaptureCapabilityMatrix.Select` は指定 backend の行だけを返す。未確認条件と probe-only backend は明示的に unavailable とし、別 backend へ fallback しない。
- reference machine の WGC windowed は `Confirmed`／`Available`、最小化は frame 供給停止の実測に基づき `Unsupported`／`Unavailable` とした。
- borderless、fullscreen、DPI、HDR、multi-monitor、遮蔽は個別 live 実測がないため `Unverified` のままにした。Desktop Duplication と GDI は probe 成立を `Confirmed` と記録するが、t03 の採否まで `ProbedOnly` に留める。

## 最終試験

```text
dotnet test tests/OpenLogicool.Capture.Matrix.Tests/OpenLogicool.Capture.Matrix.Tests.csproj --no-restore --nologo --logger "console;verbosity=normal"
```

結果: **5/5 green**（0 failed）。確認内容:

- WGC windowed の確認済み・利用可能 route
- 最小化の明示 Unsupported と理由
- HDR 未確認時に WGC windowed へ fallback しないこと
- probe 済み Desktop Duplication を製品 route と誤認しないこと
- matrix 未登録行を Unverified／Unavailable として返すこと

`git diff --check` も成功した。

## 変更ファイル

- `docs/contracts/capture-support-matrix.md`
- `src/OpenLogicool.Contracts/Capture/CaptureCapabilities.cs`
- `src/OpenLogicool.Capture/CaptureCapabilityMatrix.cs`
- `tests/OpenLogicool.Capture.Matrix.Tests/OpenLogicool.Capture.Matrix.Tests.csproj`
- `tests/OpenLogicool.Capture.Matrix.Tests/CaptureCapabilityMatrixTests.cs`
- 本証跡

## 範囲外

t03 の alternate backend 製品化採否、borderless／fullscreen／DPI／HDR／multi-monitor／遮蔽の live 実測、および capture fault の連続性停止は別 ToDo の範囲である。
