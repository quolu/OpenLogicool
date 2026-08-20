# t02-png-corpus-metrics

## 実施

- tracked asset `fixtures/frames/gamelab-main-menu-20260815.png` を Conformance test の出力へ Content としてコピーした。
- PNG を `Format32bppArgb` で BGRA8 に読み、事前固定した raw pixel SHA-256
  `89A84343CCB27E7338F3AD7EFD52B25B4D427B1AF25F3018D204B7F6BF913816` の fixture rule で
  `FrozenMetricRunner` に渡す focused test を追加した。
- acceptance corpus はこの PNG だけで構成し、training/calibration API には渡していない。

## 検証

一時 artifacts path を指定して次を実行した（worktree に NuGet／build 生成物を残さないため）。

```powershell
dotnet test .\tests\OpenLogicool.Conformance.Tests\OpenLogicool.Conformance.Tests.csproj --no-restore --artifacts-path C:\Users\kite_\AppData\Local\Temp\openlogicool-t02-artifacts-54228 --filter "FullyQualifiedName~FrozenMetricRunnerTests" --logger "console;verbosity=normal"
```

結果: 3 passed / 0 failed。

| 指標 | 結果 |
| --- | ---: |
| KnownMisclassifications | 0 |
| UnknownPromotions | 0 |
| SuccessFalsePositives | 0 |

## 変更ファイル

- `tests/OpenLogicool.Conformance.Tests/FrozenMetricRunnerTests.cs`
- `tests/OpenLogicool.Conformance.Tests/OpenLogicool.Conformance.Tests.csproj`
- `evidence/phase5-unverified/t02-png-corpus-metrics.md`
